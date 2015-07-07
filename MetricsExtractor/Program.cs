using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ArchiMetrics.Analysis;
using ArchiMetrics.Common;
using ArchiMetrics.Common.Metrics;
using MetricsExtractor.Core.Integration.Abstract;
using MetricsExtractor.Core.Integration.Concrete;
using MetricsExtractor.Custom;
using MetricsExtractor.ReportTemplate;
using Microsoft.CodeAnalysis.Options;

namespace MetricsExtractor
{
    class Program
    {
        private static readonly List<ClassRank> ClassRanks = Enum.GetValues(typeof(ClassRank)).Cast<ClassRank>().ToList();
        private static readonly string ApplicationPath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);

        static void Main(string[] args)
        {
            if (!args.Any())
            {
                Console.WriteLine("No solution configured");
                return;
            }

            var metricConfiguration = DashedParameterSerializer.Deserialize<MetricConfiguration>(args);

            if (metricConfiguration.DestinationReportPath != null && !Directory.Exists(metricConfiguration.DestinationReportPath))
                throw new DirectoryNotFoundException("Destination Report Path not found.");


            var runCodeMetrics = RunCodeMetrics(metricConfiguration);
            runCodeMetrics.Wait();
            Console.WriteLine("All projects measure, creating report");
            var ignoredNamespaces = metricConfiguration.IgnoredNamespaces ?? Enumerable.Empty<string>();
            var namespaceMetrics = runCodeMetrics.Result.Where(nm => !ignoredNamespaces.Contains(nm.Name)).ToList();

            var types = namespaceMetrics.SelectMany(x => x.TypeMetrics, (nm, t) => new TypeMetricWithNamespace(t).WithNamespace(nm.Name)).Distinct().ToList();

            const int MAX_LINES_OF_CODE_ON_METHOD = 30;

            var metodos = types.SelectMany(x => x.MemberMetrics, (type, member) => new MetodoComTipo { Tipo = type, Metodo = member }).ToList();

            var metodosRuins = GetBadMethods(metodos, MAX_LINES_OF_CODE_ON_METHOD);

            var resultadoGeral = CreateEstadoDoProjeto(types, metodosRuins, metodos.Count, namespaceMetrics);

            var reportPath = GenerateReport(resultadoGeral, metricConfiguration.DestinationReportPath ?? metricConfiguration.SolutionDirectory);

            Console.WriteLine("Report generated in: {0}", reportPath);

            #region Send to S3
            if (metricConfiguration.SendToS3.GetValueOrDefault(false))
            {
                IAmazonS3Integration amazonS3Integration = null;
                if (metricConfiguration.AwsAccessKey == null)
                    throw new ArgumentNullException("AwsAccessKey", "When SendToS3 is true, AwsAccesskey is required");
                if (metricConfiguration.AwsSecretKey == null)
                    throw new ArgumentNullException("AwsSecretKey", "When SendToS3 is true, AwsSecretKey is required");
                if (metricConfiguration.BucketS3 == null)
                    throw new ArgumentNullException("BucketS3", "When SendToS3 is true, BucketS3 is required");
                if (metricConfiguration.PathOnBucketS3 == null)
                    throw new ArgumentNullException("PathOnBucketS3", "When SendToS3 is true, PathOnBucketS3 is required");

                amazonS3Integration = new AmazonS3Integration(metricConfiguration.AwsAccessKey, metricConfiguration.AwsSecretKey);
                var pathOnS3 = Path.Combine(string.Format("{0}/metrics-{1}", metricConfiguration.PathOnBucketS3, string.Format("{0:yy-MM-dd_HH-mm}", DateTime.Now)));

                amazonS3Integration.SendDocument(reportPath, metricConfiguration.BucketS3, pathOnS3);
            }

            #endregion


            if (metricConfiguration.OpenReport.GetValueOrDefault(false))
                Process.Start(reportPath);
        }

        private static string GenerateReport(EstadoDoProjeto resultadoGeral, string solutionDirectory, bool namedDateToDirectory = true)
        {
            var reportDirectory = Path.Combine(solutionDirectory, string.Format("{0}{1}", "CodeMetricsReport",
                namedDateToDirectory ? string.Format("-{0:yy-MM-dd-HH-mm}", DateTime.Now) : ""));
            var reportPath = Path.Combine(reportDirectory, string.Format("{0}{1}.zip", "CodeMetricsReport",
                namedDateToDirectory ? string.Format("-{0:yy-MM-dd-HH-mm}", DateTime.Now) : ""));

            var reportTemplateFactory = new ReportTemplateFactory();
            var report = reportTemplateFactory.GetReport(resultadoGeral);
            var list = new[] { "*.css", "*.js" }.SelectMany(ext => Directory.GetFiles(Path.Combine(ApplicationPath, "ReportTemplate"), ext)).ToList();


            Directory.CreateDirectory(reportDirectory);
            using (var zipArchive = new ZipArchive(File.OpenWrite(reportPath), ZipArchiveMode.Create))
            {
                foreach (var item in list)
                {
                    var fileName = Path.GetFileName(item);
                    zipArchive.CreateEntryFromFile(item, fileName);
                    File.Copy(item, Path.Combine(reportDirectory, fileName), true);
                }
                var archiveEntry = zipArchive.CreateEntry("Index.html");
                using (var stream = archiveEntry.Open())
                using (var streamWriter = new StreamWriter(stream, Encoding.UTF8))
                    streamWriter.Write(report);
                reportPath = Path.Combine(reportDirectory, "Index.html");
                File.WriteAllText(reportPath, report, Encoding.UTF8);                
                zipArchive.Dispose();
            }
            return reportPath;
        }

        private static Dictionary<ClassRank, List<TypeMetricWithNamespace>> GetClassesGroupedByRank(List<TypeMetricWithNamespace> types)
        {
            var classesGroupedByRank = new Dictionary<ClassRank, List<TypeMetricWithNamespace>>();
            ClassRanks.ForEach(c => classesGroupedByRank.Add(c, new List<TypeMetricWithNamespace>()));
            var classRankings = CreateClassesRank(types).ToList();
            foreach (var group in classRankings.GroupBy(c => c.Rank))
                classesGroupedByRank[@group.Key].AddRange(@group);
            return classesGroupedByRank;
        }

        private static EstadoDoProjeto CreateEstadoDoProjeto(List<TypeMetricWithNamespace> types, List<MetodoRuim> metodosRuins, int totalDeMetodos, IEnumerable<INamespaceMetric> namespaceMetrics)
        {
            var resultadoGeral = new EstadoDoProjeto
            {
                Manutenibilidade = (int)types.Average(x => x.MaintainabilityIndex),
                LinhasDeCodigo = types.Sum(x => x.SourceLinesOfCode),
                ProfuDeHeranca = types.Average(x => x.DepthOfInheritance),
                MetodosRuins = metodosRuins,
                TotalDeMetodos = totalDeMetodos,
                TypesWithMetrics = GetClassesGroupedByRank(types)
            };
            return resultadoGeral;
        }

        private static List<MetodoRuim> GetBadMethods(List<MetodoComTipo> metodos, int maxLinesOfCodeOnMethod)
        {
            var metodosRuins = metodos
                .Where(x => (x.Metodo.SourceLinesOfCode >= maxLinesOfCodeOnMethod) || (x.Metodo.CyclomaticComplexity >= 10))
                .Select(x => new MetodoRuim
                {
                    ClassName = x.Tipo.FullName,
                    NomeMetodo = x.Metodo.Name,
                    Complexidade = x.Metodo.CyclomaticComplexity,
                    Manutenibilidade = x.Metodo.MaintainabilityIndex,
                    QuantidadeDeLinhas = x.Metodo.SourceLinesOfCode,
                })
                .OrderByDescending(x => x.QuantidadeDeLinhas).ThenByDescending(x => x.Complexidade)
                .ToList();
            return metodosRuins;
        }

        private static async Task<IEnumerable<INamespaceMetric>> RunCodeMetrics(MetricConfiguration configuration)
        {
            Console.WriteLine("Loading Solution");
            var solutionProvider = new SolutionProvider();
            var solution = await solutionProvider.Get(configuration.Solution).ConfigureAwait(false);
            Console.WriteLine("Solution loaded");

            var projects = solution.Projects.Where(p => !configuration.IgnoredProjects.Contains(p.Name)).ToList();

            Console.WriteLine("Loading metrics, wait it may take a while.");

            var metrics = new List<IEnumerable<INamespaceMetric>>();
            var metricsCalculator = new CodeMetricsCalculator(new CalculationConfiguration
            {
                NamespacesIgnored = configuration.IgnoredNamespaces,
                TypesIgnored = configuration.IgnoredTypes
            });
            foreach (var project in projects)
            {
                var calculate = await metricsCalculator.Calculate(project, solution);
                metrics.Add(calculate);
            }

            return metrics.SelectMany(nm => nm);
        }

        private static IEnumerable<TypeMetricWithNamespace> CreateClassesRank(List<TypeMetricWithNamespace> types)
        {
            return from type in types
                   let maintainabilityIndex = type.MaintainabilityIndex
                   select type.WithRank(ClassRanks.FirstOrDefault(r => (int)r >= maintainabilityIndex));
        }
    }
}