using System;
using System.Threading;

using LunarLabs.WebServer.Core;
using LunarLabs.WebServer.HTTP;
using System.Collections.Generic;
using LunarLabs.WebServer.Templates;
using System.IO;
using Phantasma.Blockchain.Contracts;
using Phantasma.Core.Types;
using Phantasma.Numerics;
using Phantasma.Cryptography;
using Phantasma.Domain;
using Phantasma.VM;
using Phantasma.Storage;
using Phantasma.VM.Utils;
using System.Text.Json;

namespace Phantasma.Docs
{

    public struct Offer
    {
        public string ID;
        public Address seller;
        public string sellSymbol;
        public string sellAmount;
        public string buySymbol;
        public string buyAmount;

        public Offer(string iD, Address seller, string sellSymbol, string sellAmount, string buySymbol, string buyAmount)
        {
            ID = iD;
            this.seller = seller;
            this.sellSymbol = sellSymbol;
            this.sellAmount = sellAmount;
            this.buySymbol = buySymbol;
            this.buyAmount = buyAmount;
        }
    }

    class Program
    {

        const string LanguageHeader = "Accept-Language";
        static SDK.API phantasmaAPI;

        const float ONE_MIN = 60f;
        const float TEN_MIN = 600f;
        const float HALF_AN_HOUR = 1800f;
        const float ONE_HOUR = 3600f;

        static string DetectLanguage(HTTPRequest request)
        {
            if (request.headers.ContainsKey(LanguageHeader))
            {
                var languages = request.headers[LanguageHeader].Split(new char[] { ',', ';' });
                foreach (var lang in languages)
                {
                    string code;
                    if (lang.Contains("-"))
                    {
                        code = lang.Split('-')[0];
                    }
                    else
                    {
                        code = lang;
                    }

                    if (LocalizationManager.HasLanguage(code))
                    {
                        return code;
                    }
                }
            }

            return "en";
        }

        static List<Offer> offers = new List<Offer>();

        static void Main(string[] args)
        {
            Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;

            if (args.Length == 0)
            {
                args = new string[] { 
                    "--path=" + Path.GetFullPath("../Frontend"),
                    "--api=http://localhost:7077/rpc",
                };
            }

            string apiHost = null;

            var apiTag = "--api";
            foreach (var arg in args)
            {
                if (arg.StartsWith(apiTag))
                {
                    apiHost = arg.Substring(apiTag.Length + 1);
                }
            }

            if (string.IsNullOrEmpty(apiHost))
            {
                Console.WriteLine("Please insert a valid --api argument, should be URL pointing to a valid Phantasma RPC node");
                Environment.Exit(-1);
            }

            var settings = ServerSettings.Parse(args);

            var server = new HTTPServer(settings, ConsoleLogger.Write);

            var templateEngine = new TemplateEngine(server, "views");

            Console.WriteLine("Frontend path: " + settings.Path);
            Console.WriteLine("Phantasma RPC: " + apiHost);

            /*var locFiles = Directory.GetFiles("Localization", "*.csv");
            foreach (var fileName in locFiles)
            {
                var language = Path.GetFileNameWithoutExtension(fileName).Split("_")[1];
                LocalizationManager.LoadLocalization(language, fileName);
            }*/

            phantasmaAPI = new SDK.API(apiHost);
            GetAllOTC();

            Func<HTTPRequest, Dictionary<string, object>> GetContext = (request) =>
            {

                var context = new Dictionary<string, object>();

                context["available_languages"] = LocalizationManager.Languages;

                var langCode = request.session.GetString("language", "auto");

                if (langCode == "auto")
                {
                    langCode = DetectLanguage(request);
                    request.session.SetString("language", langCode);
                }

                context["current_language"] = LocalizationManager.GetLanguage(langCode);

                context["offers"] = offers;

                var userAddr = request.session.GetString("userAddr", "empty");
                if (userAddr == "empty")
                {
                    userAddr = request.GetVariable("userAddr");
                    request.session.SetString("userAddr", userAddr);
                }
                context["userAddr"] = userAddr;

                var provider = request.session.GetString("provider", "none");
                if (provider == "none")
                {
                    provider = request.GetVariable("provider");
                    request.session.SetString("provider", provider);
                }
                context["provider"] = provider;

                var connector = request.session.GetString("connector", "none");
                if (connector == "none")
                {
                    connector = request.GetVariable("connector");
                    request.session.SetString("connector", connector);
                }
                context["connector"] = connector;


                var logged = request.session.GetBool("logged", false);
                context["logged"] = logged;

                return context;
            };

            server.Get("/language/{code}", (request) =>
            {
                var code = request.GetVariable("code");

                if (LocalizationManager.GetLanguage(code) != null)
                {
                    request.session.SetString("language", code);
                }

                return HTTPResponse.Redirect("/");
            });

            server.Get("/", (request) =>
            {
                var context = GetContext(request);
                return templateEngine.Render(context, "main");
            });

            server.Post("/login", (request) =>
            {
                var userAddr = request.GetVariable("address");
                var provider = request.GetVariable("provider");
                var connector = request.GetVariable("connector");

                if (userAddr != null && provider != null && connector != null)
                {
                    request.session.SetString("userAddr", userAddr);
                    request.session.SetString("provider", provider);
                    request.session.SetString("connector", connector);
                    request.session.SetBool("logged", true);
                }
                return HTTPResponse.FromString("{login:true}");
            });

            server.Post("/logout", (request) =>
            {
                var logged = request.session.GetBool("logged", false);

                if (logged)
                {
                    request.session.Remove("userAddr");
                    request.session.Remove("provider");
                    request.session.Remove("connector");
                    request.session.Remove("logged");
                }

                return HTTPResponse.FromString("{logout:true}");
            });

            server.Get("/offers", (request) =>
            {
                GetAllOTC();
                var context = GetContext(request);
                return templateEngine.Render(context, "offer");
            });

            server.Get("/offers/json", (request) =>
            {
                GetAllOTC();
                var js = new JsonSerializerOptions();
                js.WriteIndented = true;
                js.IgnoreReadOnlyProperties = false;
                js.IgnoreNullValues = false;
                var json = JsonSerializer.Serialize(offers, js);

                string output="[";
                offers.ForEach((offer) =>
                {
                    output += "{";
                    output += $"id:'{offer.ID}',";
                    output += $"seller:'{offer.seller}',";
                    output += $"sellSymbol:'{offer.sellSymbol}',";
                    output += $"sellAmount:{offer.sellAmount},";
                    output += $"buySymbol:'{offer.buySymbol}',";
                    output += $"buyAmount:{offer.buyAmount}";
                    output += "},";
                });
                output.Remove(output.Length - 1);
                output += "]";
                return HTTPResponse.FromString(output);
            });

            server.Run();

            bool running = true;

            Console.CancelKeyPress += delegate {
                server.Stop();
                running = false;
            };


            float time = 0;
            while (running)
            {
                Thread.Sleep(1000); // 1000m = 1sec 
                time++;
                if (time <= HALF_AN_HOUR)
                    GetAllOTC();
            }
        }

        private static void GetAllOTC()
        {
            offers.Clear();
            var myScript = new ScriptBuilder()
                .CallContract(NativeContractKind.Exchange.GetContractName(), "GetOTC")
                .EndScript();
            var scriptStr = Base16.Encode(myScript);
            phantasmaAPI.InvokeRawScript(DomainSettings.RootChainName, scriptStr, (script) =>
            {
                var bytes = Base16.Decode(script.results[0]);
                var orders = Serialization.Unserialize<VMObject>(bytes).ToArray<ExchangeOrder>();
                string sellAmount = "";
                string buyAmount = "";
                foreach (var offer in orders)
                {
                    offers.Add(new Offer(offer.Uid.ToString(), offer.Creator, 
                        offer.BaseSymbol, UnitConversion.ToDecimal(offer.Price, GetDecimals(offer.BaseSymbol)).ToString(), 
                        offer.QuoteSymbol, UnitConversion.ToDecimal(offer.Amount, GetDecimals(offer.QuoteSymbol)).ToString()));
                }
            });
        }

        public static int GetDecimals(string symbol)
        {
            switch (symbol)
            {
                case "GOATI":
                    return 3;
                case "SOUL":
                case "GAS":
                case "GM":
                    return 8;
                case "KCAL": 
                    return 10;
                case "BNB": 
                case "ETH": 
                case "DAI":
                case "HOD":
                    return 18;
                case "NEO": 
                case "MKNI":
                    return 0;
                case "USDT": 
                case "USDC":
                    return 6;
                default: throw new System.Exception("Unknown decimals for " + symbol);
            }
        }
    }
}
