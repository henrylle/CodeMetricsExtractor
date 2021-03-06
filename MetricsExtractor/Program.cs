﻿using System;
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

        static int Main(string[] args)
        {
            if (!args.Any())
            {
                Console.WriteLine("No solution configured");
                return 0;
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

            var reportDirectory = Path.Combine(metricConfiguration.DestinationReportPath ?? metricConfiguration.SolutionDirectory, string.Format("{0}{1}", "CodeMetricsReport", string.Format("-{0:yy-MM-dd-HH-mm}", DateTime.Now)));

            var reportPath = GenerateReport(resultadoGeral, reportDirectory);

            Console.WriteLine("Report generated in: {0}", reportPath);

            #region Send to S3
            var pathOnS3 = "";
            if (metricConfiguration.SendToS3.GetValueOrDefault(false))
            {
                SendToS3(metricConfiguration, reportPath, reportDirectory, out pathOnS3);
            }
            #endregion

            #region Send Sign Url to Slack
            if (metricConfiguration.SendSignedUrlToSlack.GetValueOrDefault(false))
            {
                SendToSlack(metricConfiguration, pathOnS3);
            }
            #endregion

            if (metricConfiguration.OpenReport.GetValueOrDefault(false))
                Process.Start(reportPath);

            return resultadoGeral.Manutenibilidade;
        }

        private static void SendToS3(MetricConfiguration metricConfiguration, string reportPath, string reportDirectory, out string pathOnS3)
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
            pathOnS3 = Path.Combine(string.Format("{0}/metrics-{1}", metricConfiguration.PathOnBucketS3, string.Format("{0:yy-MM-dd_HH-mm}", DateTime.Now)));
            amazonS3Integration.SendDocument(reportPath, metricConfiguration.BucketS3, pathOnS3);
            amazonS3Integration.SendDocument(string.Format(@"{0}\site.css", reportDirectory), metricConfiguration.BucketS3, pathOnS3, "site.css", true);
        }

        private static void SendToSlack(MetricConfiguration metricConfiguration, string pathOnS3)
        {
            ISlackIntegration slackIntegration = null;
            IAmazonS3Integration amazonS3Integration = null;

            if (metricConfiguration.SlackChannel == null)
                throw new ArgumentNullException("SendToS3", "When SendSignUrlToSlack is true, SendToS3 enabled is required");
            if (metricConfiguration.SlackChannel == null)
                throw new ArgumentNullException("SlackChannel", "When SendSignUrlToSlack is true, SlackChannel is required");
            if (metricConfiguration.SlackMessage == null)
                throw new ArgumentNullException("SlackMessage", "When SendSignUrlToSlack is true, SlackMessage is required");
            if (metricConfiguration.SlackToken == null)
                throw new ArgumentNullException("SlackToken", "When SendSignUrlToSlack is true, SlackToken is required");
            if (metricConfiguration.SlackUserName == null)
                throw new ArgumentNullException("SlackUserName", "When SendSignUrlToSlack is true, SlackUserName is required");

            amazonS3Integration = new AmazonS3Integration(metricConfiguration.AwsAccessKey, metricConfiguration.AwsSecretKey);
            var signedUrl = amazonS3Integration.SignUrl(pathOnS3, "index.html", metricConfiguration.BucketS3,
                metricConfiguration.SlackUrlExpirationInSeconds.GetValueOrDefault(86400));

            var dtExpirationLink =
                DateTime.Now.AddSeconds(metricConfiguration.SlackUrlExpirationInSeconds.GetValueOrDefault(86400));

            Console.WriteLine("Signed Url Metrics Report generated. Date Expiration: {0:u}.", dtExpirationLink);

            slackIntegration = new SlackIntegration(metricConfiguration.SlackToken);
            slackIntegration.PostMessage(metricConfiguration.SlackChannel, string.Format("{0}{1}. Link expire at {2}: ", metricConfiguration.SlackMessage, signedUrl, dtExpirationLink),
                metricConfiguration.SlackUserName);


            Console.WriteLine("Link Url Metrics Report sent to Slack: {0}. Dt Expiration", signedUrl);
        }

        private static string GenerateReport(EstadoDoProjeto resultadoGeral, string reportDirectory)
        {

            var reportPath = Path.Combine(reportDirectory, string.Format("{0}{1}.zip", "CodeMetricsReport", string.Format("-{0:yy-MM-dd-HH-mm}", DateTime.Now)));

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