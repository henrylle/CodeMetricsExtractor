using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using MetricsExtractor.Core.Integration.Abstract;
using MetricsExtractor.Core.Integration.Concrete;
using NUnit.Framework;

namespace MetricsExtractor.Test.Core
{
    [TestFixture]
    public class SlackIntegrationTest
    {
        [Test]
        public void CanPostMessageToChannelTest()
        {
            //Arrange
            string token = "xoxp-6704290194-6704290242-6795193635-107a63";
            ISlackIntegration slackIntegration = new SlackIntegration(token);

            //Act
            var result = slackIntegration.PostMessage("#fortesdoc", "teste de mensagem", "fortesbot");

            //Asserts
            Assert.IsNotNull(result);
            Console.WriteLine(result);
        }
    }
}
