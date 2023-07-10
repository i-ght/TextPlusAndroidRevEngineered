using LibTextPlus;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace textplus.filterdata
{
    class Program
    {
        private static void FilterMalesWithFirstNames()
        {
            using var strem = new StreamReader(@"Z:\win7_share\src\csharp\textplus\src\textplus.filterdata\bin\x64\Debug\netcoreapp3.0\textplus-data-male.txt");
            var output = new List<string>();
            var cnt = 0;
            while (!strem.EndOfStream) {
                var yayson = JsonConvert.DeserializeObject<DeetUserInfoResp>(strem.ReadLine());
                cnt++;
                var firstName = yayson.primaryPersona.firstName;
                var lastName = yayson.primaryPersona.lastName;
                //if (string.IsNullOrWhiteSpace(firstName))
                //    continue;
                var cells = yayson.matchables.Where(item => item.type == "PHONE");
                var voips = yayson.primaryPersona.tptns;
                if (!cells.Any() && !voips.Any())
                    continue;

                if (cells.Count() > 1 || voips.Count() > 1) {

                }

                //output.AddRange(cells.Select(item => item.value));
                //output.AddRange(voips.Select(item => item.phoneNumber));

                var cellNumber = cells.LastOrDefault() == default ? "" : cells.FirstOrDefault().value;
                var voip = voips.LastOrDefault() == default ? "" : voips.FirstOrDefault().phoneNumber;

                output.Add($"{yayson.primaryPersona.handle},{firstName} {lastName},{cellNumber},{voip}");
            }

            using var writer = new StreamWriter("textplus-numbers.txt");
            foreach (var item in output) {
                writer.WriteLine(item);
            }
        }
        private static void Main(string[] args)
        {
            FilterMalesWithFirstNames();
        }
    }
}
