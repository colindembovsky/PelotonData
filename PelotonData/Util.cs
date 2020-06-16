using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PelotonData
{
    public static class Util
    {
        public static DateTime DateTimeFromEpochSeconds(int seconds)
        {
            DateTime dateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, System.DateTimeKind.Utc);
            return dateTime.AddSeconds(seconds);
        }

        public static void WriteDictionaryAsCSV(Dictionary<string, string> dict, string path)
        {
            var text = "Name, Value\n" + 
                string.Join("", dict.OrderBy(kvp => kvp.Key).Select(kvp => $"{kvp.Key}, {kvp.Value}\n"));

            File.WriteAllText(path, text);
        }

        public static Dictionary<string, string> GetObjectPropertiesAsDictionaryRecursive(object o)
        {


            var dict = new Dictionary<string, string>();
            foreach (var prop in o.GetType().GetProperties())
            {
                if (prop.PropertyType.IsPrimitive || prop.PropertyType.Name == "String")
                {
                    var value = prop.GetValue(o);
                    if (value != null)
                    {
                        dict[prop.Name] = prop.GetValue(o).ToString().Replace(",", "<COMMA>");
                    }
                    else
                    {
                        dict[prop.Name] = "";
                    }
                } // if the property is NOT an enumerable, then descend into it. 
                else if (!typeof(System.Collections.IEnumerable).IsAssignableFrom(prop.PropertyType))
                {
                    var value = prop.GetValue(o);
                    if (value == null) continue;
                    if (value.GetType().Name != "String")
                    {
                        var nestedDict = GetObjectPropertiesAsDictionaryRecursive(value);
                        foreach (var kvp in nestedDict)
                        {
                            dict[$"{prop.Name}.{kvp.Key}"] = kvp.Value;
                        }
                    }
                    else
                    {
                        dict[prop.Name] = value.ToString().Replace(",", "<COMMA>");
                    }
                }

            }
            return dict;
        }
    }
}
