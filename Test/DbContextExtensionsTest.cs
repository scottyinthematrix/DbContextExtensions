using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ScottyApps.ScottyBlogging.Entity;

namespace ScottyApps.Utilities.DbContextExtentionsTest
{
    [TestClass]
    public class DbContextExtensionsTest
    {
        [TestMethod]
        public void TestGetAllowedEntityTypes()
        {
            var ctx = new BloggingContext("Blogging");
            var allowedTypes = DbContextExtensions.DbContextExtensions.GetAllowedEntityTypes<BloggingContext>(ctx);
            Assert.IsNotNull(allowedTypes);
            Assert.IsTrue(allowedTypes.Count > 0);
        }
    }
}
