using System.IO;
using System.Linq;
using System.Text;
using Placemaker.Core;
using Xunit;
namespace Placemaker.Tests
{
    public class ZzDumpTmp
    {
        [Fact]
        public void Dump()
        {
            var lang = TestKit.Lang();
            var sb = new StringBuilder();
            foreach (var g in GeneratorRegistry.All)
            {
                sb.AppendLine("== " + g.Id);
                foreach (var c in g.Controls)
                {
                    lang.TryGetValue(c.LabelKey ?? "", out var l);
                    string t = "";
                    if (!string.IsNullOrEmpty(c.TooltipKey)) lang.TryGetValue(c.TooltipKey, out t);
                    sb.AppendLine($"{c.Id}|{c.Tier}|{c.Group}|{c.LabelKey}|{l}|{(string.IsNullOrEmpty(c.TooltipKey) ? "-" : "T")}|{t}|{c.VisibleWhen}");
                }
            }
            File.WriteAllText(System.Environment.GetEnvironmentVariable("DUMP_OUT") ?? "dump.txt", sb.ToString());
        }
    }
}
