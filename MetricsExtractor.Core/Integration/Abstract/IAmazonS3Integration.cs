﻿namespace MetricsExtractor.Core.Integration.Abstract
{
    public interface IAmazonS3Integration
    {
        void SendDocument(string filePath, string bucket, string destinationPath, string fileNamOnDestinationWithExtension = "index.html", bool isPublic = false);

        void SendDirectory(string directory, string bucket, string destinationPath);

        string SignUrl(string path, string docName, string bucket, int segundosParaExpiracao = 30*24*60*60/*30 days*/);
    }
}
