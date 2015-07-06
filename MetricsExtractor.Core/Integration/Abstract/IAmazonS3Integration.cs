using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MetricsExtractor.Core.Integration.Abstract
{
    public interface IAmazonS3Integration
    {
        void SendDocument(string filePath, string bucket, string destinationPath, string fileNamOnDestinationWithExtension="index.html");

        string SignUrl(string path, string docName, string bucket, int segundosParaExpiracao = 30*24*60*60/*30 days*/);
    }
}
