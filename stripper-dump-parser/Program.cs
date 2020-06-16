using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace stripper_dump_parser
{
    public class EntityAction
    {
        public string target;
        public string input;
        public string value;
        public string delay;
        public string limit;
    }

    class Program
    {
        static readonly string myself = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        static readonly string worker = Path.Combine(myself, "worker");
        static readonly string output = Path.Combine(myself, "output");

        #region FGD Outputs
        static readonly List<string> outputs = new List<string>
        {
            "onuse",
            "onroundended"
        };
        #endregion

        static void Main()
        {
            Console.Title = "Stripper Dump Paser by Kyle";

            Directory.CreateDirectory(worker);
            Directory.CreateDirectory(output);

            ParseFgd(Path.Combine(myself, "base.fgd"));

            var regex = new Regex("[a-zA-Z0-9_]{3,}.[0-9]{4}");

            Directory.GetFiles(worker, "*.cfg", SearchOption.TopDirectoryOnly).ToList().ForEach(path =>
            {
                var file = Path.GetFileName(path);

                if (!regex.IsMatch(file))
                {
                    Console.WriteLine("File [{0}] not match with rule.", file);
                    return;
                }

                ParseStripper(path, file.Substring(0, file.Length - 9));
            });

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("All done.");
            Console.ReadKey(true);
        }

        private static void ParseFgd(string file)
        {
            if (!File.Exists(file))
                return;

            var prefix = "	output ";
            var blacks = new[] {"distance", "velocity"};
            File.ReadAllLines(file).ToList().ForEach(line =>
            {
                if (!line.StartsWith(prefix))
                    return;

                line = line.Replace(prefix, "");
                line = line.Substring(0, line.IndexOf("(")).ToLower();

                if (Array.IndexOf(blacks, line) != -1)
                    return;

                outputs.Add(line);
            });

            file += ".out";
            if (File.Exists(file))
            {
                // delete old
                File.Delete(file);
            }
            File.WriteAllLines(file, outputs.ToArray(), new UTF8Encoding(false));
        }

        private static void ParseStripper(string file, string outfile)
        {
            var stripper = "}" + File.ReadAllText(file) + "{";

            var entities = new JObject();
            var transTxt = new List<string>();

            stripper.Split(new[] { "}{" }, StringSplitOptions.None).ToList().ForEach(split => //.First(); //
            {
                var data = new Dictionary<string, string>();
                var dict = new Dictionary<string, List<EntityAction>>();
                var name = "";
                split.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries).ToList().ForEach(line =>
                {
                    // check target name
                    if (line.StartsWith("\"targetname\""))
                    {
                        name = line.Replace("\"targetname\" ", "").Replace("\"", "");
                        return;
                    }

                    var raw = line.Split(new[] { "\" \"" }, StringSplitOptions.None);
                    if (raw.Length != 2)
                    {
                        Console.WriteLine("[{1}] Error parse line key values -> [{0}]", line, outfile);
                        return;
                    }

                    var key = raw[0].Substring(1);
                    var val = raw[1].Substring(0, raw[1].Length - 1);

                    // check output
                    if (outputs.Contains(key.ToLower()))
                    {
                        var index = line.Substring(1).IndexOf('"') + 1;
                        var thkey = line.Substring(1, index - 1); //e.g.  OnTimer
                        var thval = line.Substring(index + 2);
                        var iopts = SplitEntityAction(thval);
                        if (iopts.Length != 5)
                        {
                            Console.WriteLine("[{2}] Error parse entity action [{0}] -> [{1}]", thkey, thval, outfile);
                            return;
                        }

                        if (!dict.ContainsKey(thkey))
                        {
                            dict[thkey] = new List<EntityAction>();
                        }

                        var io = new EntityAction
                        {
                            target = iopts[0],
                            input = iopts[1],
                            value = iopts[2],
                            delay = iopts[3],
                            limit = iopts[4]
                        };

                        dict[thkey].Add(io);

                        if (io.input.ToLower().Equals("command") && io.value.ToLower().StartsWith("say"))
                        {
                            var text = io.value.Substring(4);
                            //Console.WriteLine("SayText detected -> [{0}]", text);
                            transTxt.Add(text);
                        }
                    }
                    // parse key value
                    else if(data.ContainsKey(key))
                    {
                        Console.WriteLine("[{3}] Exists -> [{0}] => [{1}] | [{2}]", key, val, data[key], outfile);
                        data[key] = val;
                    }
                    else
                    {
                        data.Add(key, val);
                    }
                });

                if (string.IsNullOrEmpty(name))
                {
                    if (data.ContainsKey("hammerid"))
                    {
                        name = "id_" + data["hammerid"];
                    }
                    else
                    {
                        name = "cc_" + entities.Count.ToString();
                    }
                }

                var jo = new JObject();

                // 普通key values 字段
                foreach (var iter in data)
                {
                    jo[iter.Key] = iter.Value;
                }

                // output 输出字段
                // 取字典key为output名
                foreach (var iter in dict)
                {
                    var ja = new JArray();

                    foreach (var it in iter.Value)
                    {
                        ja.Add(JObject.FromObject(it));
                    }

                    jo[iter.Key] = ja;
                }

                entities[name] = jo;
            });
            File.WriteAllText(Path.Combine(output, outfile + ".txt"), TranslationsToKeyValues(transTxt).Replace("\r\n", "\n"), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(output, outfile + ".json"), JsonConvert.SerializeObject(entities, Formatting.Indented).Replace("\r\n", "\n"), new UTF8Encoding(false));
        }

        private static string[] SplitEntityAction(string thval)
        {
            string[] text;

            text = thval.Substring(1, thval.Length - 2).Split(new[] { "" }, StringSplitOptions.None); // 0x1b
            if (text.Length == 5)
                return text;

            text = thval.Substring(1, thval.Length - 2).Split(new[] { "," }, StringSplitOptions.None);
            if (text.Length == 5)
                return text;

            text = thval.Substring(1, thval.Length - 2).Split(new[] { ";" }, StringSplitOptions.None);
            if (text.Length == 5)
                return text;

            return new string[] { };
        }

        // manually
        private static string TranslationsToKeyValues(List<string> transTxt)
        {
            var text = "\"Console_T\"" + Environment.NewLine + "{" + Environment.NewLine;

            transTxt.ForEach(line =>
            {
                text += Environment.NewLine;
                text += "    " + "\"" + line + "\"" + Environment.NewLine;
                text += "    " + "{" + Environment.NewLine;
                text += "    " + "    " + "\"chi\"" + " \"" + "** **" + "\"" + Environment.NewLine;
                text += "    " + "}" + Environment.NewLine;
                
            });

            text += Environment.NewLine + "}";
            return text;
        }
    }
}

/*
        {
            "onignite",
            "onuser1",
            "onuser2",
            "onuser3",
            "onuser4",
            "counter",
            "onbreak",
            "ontakedamage",
            "onhealthchanged",
            "onphyscannondetach",
            "onphyscannonanimateprestarted",
            "onphyscannonanimatepullstarted",
            "onphyscannonpullanimfinished",
            "onphyscannonanimatepoststarted",
            "ondamaged",
            "ondeath",
            "onhalfhealth",
            "onhearworld",
            "onhearplayer",
            "onhearcombat",
            "onfoundenemy",
            "onlostenemylos",
            "onlostenemy",
            "onfoundplayer",
            "onlostplayerlos",
            "onlostplayer",
            "ondamagedbyplayer",
            "ondamagedbyplayersquad",
            "ondenycommanderuse",
            "onsleep",
            "onwake",
            "onforcedinteractionstarted",
            "onforcedinteractionaborted",
            "onforcedinteractionfinished",
            "onspawnnpc",
            "onspawnnpc",
            "onallspawned",
            "onallspawneddead",
            "onalllivechildrendead",
            "onstarttouch",
            "onstarttouchall",
            "onendtouch",
            "onendtouchall",
            "onbeginfade",
            "onsurfacechangedtotarget",
            "onsurfacechangedfromtarget",
            "onplayergotonladder",
            "onplayergotoffladder",
            "ontouchedbyentity",
            "onpushedplayer",
            "onignited",
            "onextinguished",
            "onheatlevelstart",
            "onheatlevelend",
            "onshowmessage",
            "onplay",
            "onguststart",
            "ongustend",
            "onlighton",
            "onlightoff",
            "playeron",
            "playeroff",
            "pressedmoveleft",
            "pressedmoveright",
            "pressedforward",
            "pressedback",
            "pressedattack",
            "pressedattack2",
            "unpressedmoveleft",
            "unpressedmoveright",
            "unpressedforward",
            "unpressedback",
            "unpressedattack",
            "unpressedattack2",
            "xaxis",
            "yaxis",
            "attackaxis",
            "attack2axis",
            "onfoundentity",
            "onplayerinzone",
            "onplayeroutzone",
            "playersincount",
            "playersoutcount",
            "onnpcstartedusing",
            "onnpcstoppedusing",
            "onfullyopen",
            "onfullyclosed",
            "onfullyopen",
            "onfullyclosed",
            "ongetspeed",
            "ondamaged",
            "onpressed",
            "onuselocked",
            "onin",
            "onout",
            "position",
            "onpressed",
            "onunpressed",
            "onfullyclosed",
            "onfullyopen",
            "onreachedposition",
            "onclose",
            "onopen",
            "onfullyopen",
            "onfullyclosed",
            "onblockedclosing",
            "onblockedopening",
            "onunblockedclosing",
            "onunblockedopening",
            "onlockeduse",
            "onclose",
            "onopen",
            "onfullyopen",
            "onfullyclosed",
            "onblockedclosing",
            "onblockedopening",
            "onunblockedclosing",
            "onunblockedopening",
            "onlockeduse",
            "onrotationdone",
            "onmapspawn",
            "onnewgame",
            "onloadgame",
            "onmaptransition",
            "onbackgroundmap",
            "onmultinewmap",
            "onmultinewround",
            "onendfollow",
            "onlessthan",
            "onequalto",
            "onnotequalto",
            "ongreaterthan",
            "ontrue",
            "onfalse",
            "onalltrue",
            "onallfalse",
            "onmixed",
            "oncase01",
            "oncase02",
            "oncase03",
            "oncase04",
            "oncase05",
            "oncase06",
            "oncase07",
            "oncase08",
            "oncase09",
            "oncase10",
            "oncase11",
            "oncase12",
            "oncase13",
            "oncase14",
            "oncase15",
            "oncase16",
            "ondefault",
            "onequal",
            "onnotequal",
            "onspawn",
            "ontrigger",
            "onregisteredactivate1",
            "onregisteredactivate2",
            "onregisteredactivate3",
            "onregisteredactivate4",
            "onspawn",
            "ontrigger1",
            "ontrigger2",
            "ontrigger3",
            "ontrigger4",
            "ontrigger5",
            "ontrigger6",
            "ontrigger7",
            "ontrigger8",
            "ontimer",
            "ontimerhigh",
            "ontimerlow",
            "soundlevel",
            "onroutedsound",
            "onheardsound",
            "outvalue",
            "outcolor",
            "outvalue",
            "onhitmin",
            "onhitmax",
            "onchangedfrommin",
            "onchangedfrommax",
            "ongetvalue",
            "line",
            "onplaybackfinished",
            "onentityspawned",
            "onentityspawned",
            "onentityfailedspawn",
            "onpass",
            "onfail",
            "targetdir",
            "onfacinglookat",
            "onnotfacinglookat",
            "facingpercentage",
            "angularvelocity",
            "ongreaterthan",
            "ongreaterthanorequalto",
            "onlessthan",
            "onlessthanorequalto",
            "onequalto",
            "velocity",
            "onconstraintbroken",
            "ondamaged",
            "onawakened",
            "onmotionenabled",
            "onphysgunpickup",
            "onphysgunpunt",
            "onphysgunonlypickup",
            "onphysgundrop",
            "onplayeruse",
            "onbreak",
            "onactivate",
            "onawakened",
            "onconvert",
            "onattach",
            "ondetach",
            "onanimationbegun",
            "onanimationdone",
            "onmotionenabled",
            "onawakened",
            "onphysgunpickup",
            "onphysgunpunt",
            "onphysgunonlypickup",
            "onphysgundrop",
            "onplayeruse",
            "onplayerpickup",
            "onoutofworld",
            "ondeath",
            "onstart",
            "onnext",
            "onarrivedatdestinationnode",
            "ondeath",
            "onpass",
            "onchangelevel",
            "onhurt",
            "onhurtplayer",
            "onremove",
            "ontrigger",
            "ontouching",
            "onnottouching",
            "ontrigger",
            "ontrigger",
            "ontimeout",
            "impactforce",
            "nearestentitydistance",
            "oncreatenpc",
            "onfailedtocreatenpc",
            "oncreateaddon",
            "onfailedtocreateaddon",
            "oneventfired",
            "oneventfired",
            "oncreditsdone",
            "onstartslowingtime",
            "onstopslowingtime",
            "onprimaryportalplaced",
            "onsecondaryportalplaced",
            "onduck",
            "onunduck",
            "onjump",
            "onflashlighton",
            "onflashlightoff",
            "playerhealth",
            "playermissedar2altfire",
            "playerhasammo",
            "playerhasnoammo",
            "playerdied",
            "onlighton",
            "onlightoff",
            "onproxyrelay",
            "onfire",
            "onaquiretarget",
            "onlosetarget",
            "onammodepleted",
            "ongotcontroller",
            "onlostcontroller",
            "ongotplayercontroller",
            "onlostplayercontroller",
            "onreadytofire",
            "onuse",
            "onroundended"
        }
*/