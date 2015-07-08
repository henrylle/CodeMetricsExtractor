using System.IO;
using System.Linq;

namespace MetricsExtractor
{
    public class MetricConfiguration
    {
        public MetricConfiguration()
        {
            IgnoredTypes = IgnoredNamespaces = IgnoredProjects = Enumerable.Empty<string>().ToArray();
        }
        public string Solution { get; set; }

        public string SolutionDirectory { get { return Path.GetDirectoryName(Solution); } }

        public string[] IgnoredProjects { get; set; }

        public string[] IgnoredNamespaces { get; set; }

        public string[] IgnoredTypes { get; set; }

        public string DestinationReportPath { get; set; }

        public bool? OpenReport { get; set; }

        public bool? SendToS3 { get; set; }

        public string AwsAccessKey { get; set; }

        public string AwsSecretKey { get; set; }

        public string BucketS3 { get; set; }

        public string PathOnBucketS3 { get; set; }

        public bool? SendSignedUrlToSlack { get; set; }

        public int? SlackUrlExpirationInSeconds { get; set; }

        public string SlackToken { get; set; }

        public string SlackChannel { get; set; }

        public string SlackMessage { get; set; }

        public string SlackUserName { get; set; }
    }
}
