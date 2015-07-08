using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace MetricsExtractor.Test
{
    [TestFixture]
    public class DashedParameterSerializerTest
    {
        [Test]
        public void PreenchePropriedadesString()
        {
            var parametros = DashedParameterSerializer.Deserialize<Parameters>(new[] { "-nome", "Alberto", "-email", "alberto.monteiro@live.com" });

            Assert.AreEqual("Alberto", parametros.Name);
            Assert.AreEqual("alberto.monteiro@live.com", parametros.Email);
        }

        [Test]
        public void PreenchePropriedadesArray()
        {
            var parameters = DashedParameterSerializer.Deserialize<Parameters>(new[] { "-nome", "Alberto", "-telefones", "302110836;8234779", "-email", "alberto.monteiro@live.com" });

            Assert.AreEqual("Alberto", parameters.Name);
            Assert.AreEqual("alberto.monteiro@live.com", parameters.Email);
            CollectionAssert.AreEqual(new[] { "302110836", "8234779" }, parameters.PhoneNumber);
        }

        [Test]
        public void IgnoraPropriedadesInexistentes()
        {
            var parametros = DashedParameterSerializer.Deserialize<Parameters>(new[] { "-nome", "Alberto", "-naoExiste", "naoExiste", "-email", "alberto.monteiro@live.com" });

            Assert.AreEqual("Alberto", parametros.Name);
            Assert.AreEqual("alberto.monteiro@live.com", parametros.Email);
        }

        class Parameters
        {
            public string Name { get; set; }

            public string Email { get; set; }
            public string[] PhoneNumber { get; set; }
        }
    }
}
