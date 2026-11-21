using AngleSharp;
using JAFleet.Batch;
using System.Net;

namespace JAFleet.Test
{
    /// <summary>FR24に弾かれたかの判定。403のときだけバッチを中止する。</summary>
    [TestClass]
    public class Fr24AccessCheckTest
    {
        private static Task<AngleSharp.Dom.IDocument> OpenAsync(HttpStatusCode status) =>
            BrowsingContext.New().OpenAsync(res => res.Status(status).Content("<html><body></body></html>"));

        [TestMethod]
        public async Task 応答が403なら弾かれたと判定する()
        {
            Assert.IsTrue(Fr24AccessCheck.IsForbidden(await OpenAsync(HttpStatusCode.Forbidden)));
        }

        [TestMethod]
        public async Task 応答が200なら続ける()
        {
            Assert.IsFalse(Fr24AccessCheck.IsForbidden(await OpenAsync(HttpStatusCode.OK)));
        }

        [TestMethod]
        public async Task 応答が403以外のエラーなら中止の対象にしない()
        {
            Assert.IsFalse(Fr24AccessCheck.IsForbidden(await OpenAsync(HttpStatusCode.NotFound)));
            Assert.IsFalse(Fr24AccessCheck.IsForbidden(await OpenAsync(HttpStatusCode.TooManyRequests)));
        }

        [TestMethod]
        public void 応答が無ければ弾かれたとは判定しない()
        {
            Assert.IsFalse(Fr24AccessCheck.IsForbidden(null));
        }

        [TestMethod]
        public void 通知文にバッチ名とレジと中止した旨が入る()
        {
            string message = Fr24AccessCheck.BuildMessage("RefreshWorkingStatus", "JA841A", new DateTime(2026, 11, 22, 6, 0, 0));

            StringAssert.StartsWith(message, "RefreshWorkingStatus中止:");
            StringAssert.Contains(message, "JA841A");
            StringAssert.Contains(message, "403");
            StringAssert.Contains(message, "DBは書き換えていません");
        }
    }
}
