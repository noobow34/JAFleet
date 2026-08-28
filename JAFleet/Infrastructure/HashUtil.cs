using Force.Crc32;
using System.Text;

namespace JAFleet.Infrastructure
{
    public static class HashUtil
    {
        /// <summary>
        /// 文字列のCRC32を計算して文字列で返す
        /// </summary>
        /// <param name="srcStr"></param>
        /// <returns></returns>
        public static string CalcCRC32(string srcStr)
        {
            byte[] bytes = new UTF8Encoding().GetBytes(srcStr);
            uint crc32 = Crc32CAlgorithm.Compute(bytes);

            return Convert.ToString(crc32, 16);
        }
    }
}
