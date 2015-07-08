namespace MetricsExtractor.Core.Integration.Abstract
{
    public interface ISlackIntegration
    {
        string PostMessage(string channel, string message, string userName);
    }
}
