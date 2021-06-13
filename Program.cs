using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MemoryDbLibrary;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GlobalVariableProvider
{
    public class Config
    {

        public string DatabaseFile { get; set; } = null;

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public LogLevel MinLogLevel { get; set; } = LogLevel.Information;

        public string PipeName { get; set; } = null;
    }

    class Program
    {
        static MemoryDb db;
        static ILogger<Program> logger;

        static void MethodForStop(object sender, EventArgs eventArgs)
        {
            Environment.ExitCode = 0;
        }

        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.ProcessExit += MethodForStop;

            // Read config
            var configJson = File.ReadAllBytes("misc/config.json");
            var config = JsonSerializer.Deserialize<Config>(configJson);

            // Initialize logger
            using ILoggerFactory loggerFactory =
                LoggerFactory.Create(builder =>
                    builder.AddSystemdConsole(options =>
                    {
                        options.IncludeScopes = true;
                        options.TimestampFormat = "hh:mm:ss ";
                    })
                    .SetMinimumLevel(config.MinLogLevel));

            logger = loggerFactory.CreateLogger<Program>();

            logger.LogInformation("Config is read:");
            logger.LogInformation($" => Minimum log level is {config.MinLogLevel}");
            logger.LogInformation($" => Database file is {config.DatabaseFile}");
            logger.LogInformation($" => Pipe name is {config.PipeName}");

            // Initialize MemoryDb
            if(config.DatabaseFile == null)
            {
                logger.LogError("Error: Database file and/or pipe name is/are not specified in argument");
                return;
            }

            logger.LogInformation($"Initialize in-memory db with file path: {config.DatabaseFile}");
            db = new MemoryDb(config.DatabaseFile);
            if(!db.IsPersistentStorageEnabled())
            {
                logger.LogError("Error: database file cannot be located!");
                return;
            }

            logger.LogInformation(" => MemoryDb is initialized");

            db.LoadAll(true);

            logger.LogInformation(" => Database file load is done");

            // Check that FIFO is exist, if not create it
            logger.LogInformation("Named pipe initialization:");
            string pipeName = $"{config.PipeName}-in";
            logger.LogInformation($" => Input pipe name: {pipeName}");
            if(!File.Exists(pipeName))
            {
                Process proc = new Process()
                {
                    StartInfo = new ProcessStartInfo()
                    {
                        FileName = "/usr/bin/mkfifo",
                        Arguments = $"{pipeName} -m666",
                    }
                };
                proc.Start();
                proc.WaitForExit();
                if(proc.ExitCode != 0)
                {
                    logger.LogError($" => Error: mkfifo command returned with RC={proc.ExitCode}");
                    return;
                }
                logger.LogInformation($" => FIFO ({pipeName}) has been created");
            }
            else
            {
                logger.LogInformation($" => FIFO ({pipeName}) already exist, no action");
            }
            
            // Application is ready to run
            logger.LogInformation("Application is initialized\nWaiting for requests!");

            while(true)
            {
                FileStream fs_in = new FileStream(pipeName, FileMode.Open, FileAccess.Read);
                StreamReader sr_in = new StreamReader(fs_in);

                string line = sr_in.ReadLine();

                sr_in.Close();
                fs_in.Close();

                logger.LogDebug($"Receveid data: [{line}]");

                if(!string.IsNullOrEmpty(line) && !string.IsNullOrWhiteSpace(line))
                {
                    ThreadPool.QueueUserWorkItem(HandleRequest, line as object);
                }
            }
        }

        static void HandleRequest(object lineObj)
        {
            string line = lineObj as string;
            logger.LogDebug($"Received string: [{line}]");

            string[] inputs = line.Split("/tmp/globvar-", StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < inputs.Length; i++)
            {
                inputs[i] = $"/tmp/globvar-{inputs[i]}";
            }

            foreach(var input in inputs)
            {
                logger.LogDebug($"Splited input: [{input}]");
                string[] words = input.Trim().Split();

                Stopwatch sw = new Stopwatch();
                sw.Start();

                // No message was passed, do nothing
                if(words.Length == 0)
                    return;

                if(words.Length < 2)
                {                
                    if(!File.Exists(words[0]))
                    {
                        logger.LogWarning($"Specified file does not exist: {words[0]}");
                        return;
                    }

                    // Minimum 2 words needed: output pipe file and action word
                    // Return with an error message 
                    FileStream fs_out = new FileStream(words[0], FileMode.Open, FileAccess.Write);
                    StreamWriter sw_out = new StreamWriter(fs_out);

                    sw_out.WriteLine("Action word is missing. Possible actions:");
                    sw_out.WriteLine("Read record:         get <key>");
                    sw_out.WriteLine("List sub records:    getdir <key>");
                    sw_out.WriteLine("List all records:    getall");
                    sw_out.WriteLine("Create record:       set <key> <value>");
                    sw_out.WriteLine("Delete record:       set <key>");
                    sw_out.WriteLine("Delete all records:  delall");
                    sw_out.WriteLine("Delete sub records:  deldir <key>");
                    sw_out.WriteLine("Save record:         save <key>");
                    sw_out.WriteLine("Load record:         load <key> <override: true/false>");
                    sw_out.WriteLine("Load all records:    loadall <override: true/false");
                    sw_out.WriteLine("Purge from file:     purge <key>");

                    sw_out.Close();
                    fs_out.Close();
                }
                else
                {
                    FileStream fs_out = new FileStream(words[0], FileMode.Open, FileAccess.Write);
                    StreamWriter sw_out = new StreamWriter(fs_out);

                    List<string> output = new List<string>();

                    // Request is OK for weight, check its syntax and do the proper action
                    switch(words[1])
                    {
                        case "get":
                            if(words.Length < 3)
                            {
                                sw_out.WriteLine("ERROR: Missing key");
                            }
                            else
                            {
                                var item = db.Select(words[2]);
                                if(item.Value != null)
                                    sw_out.WriteLine($"{item.Key} {item.Value}");
                            }
                            break;
                        case "getdir":
                            if(words.Length < 3)
                            {
                                sw_out.WriteLine("ERROR: Missing key");
                            }
                            else
                            {
                                var list = db.ListDir(words[2]);
                                foreach(var item in list)
                                {
                                    sw_out.WriteLine($"{item.Key} {item.Value}");
                                }
                            }
                            break;
                        case "getall":
                            {
                                var list = db.ListAll();
                                foreach(var item in list)
                                {
                                    sw_out.WriteLine($"{item.Key} {item.Value}");
                                }
                            }
                            break;
                        case "set":
                            if(words.Length < 3)
                            {
                                sw_out.WriteLine("ERROR: Missing key");
                            }
                            else
                            {
                                if(words.Length == 3)
                                {
                                    db.Add(words[2], null);
                                    sw_out.WriteLine($"Variable ({words[2]}) is deleted");
                                }
                                else
                                {
                                    string value = "";
                                    for(int i = 3; i < words.Length; i++)
                                    {
                                        value += $"{words[i]} ";
                                    }
                                    value = value.Trim();
                                    db.Add(words[2], value);
                                    sw_out.WriteLine($"Variable is added: {words[2]} -> {value}");
                                }
                            }
                            break;
                        case "delall":
                            db.RemoveAll();
                            sw_out.WriteLine($"Database is purged");
                            break;
                        case "deldir":
                            if(words.Length < 3)
                            {
                                sw_out.WriteLine("ERROR: Missing key");
                            }
                            else
                            {
                                db.RemoveDir(words[2]);
                                sw_out.WriteLine($"Directory ({words[2]}) is purged");
                            }
                            break;
                        case "save":
                            if(words.Length < 3)
                            {
                                sw_out.WriteLine("ERROR: Missing key");
                            }
                            else
                            {
                                var status = db.Save(words[2]);
                                if(status.Status == true)
                                    sw_out.WriteLine($"Save is done: {status.Message}");
                                else
                                    sw_out.WriteLine($"ERROR: Save is failed: {status.Message}");
                            }
                            break;
                        case "load":
                            if(words.Length < 4)
                            {
                                sw_out.WriteLine("ERROR: Missing key or override value");
                            }
                            else
                            {
                                bool? replace = words[3] == "true" ? true : words[3] == "false" ? false : null;
                                if(replace == null)
                                {
                                    sw_out.WriteLine("ERROR: override can be true or false");
                                }
                                else
                                {
                                    var status = db.Load(replace ?? false, words[2]);
                                    if(status.Status == true)
                                        sw_out.WriteLine($"Load is done: {status.Message}");
                                    else
                                        sw_out.WriteLine($"ERROR: Load is failed: {status.Message}");
                                }
                            }
                            break;
                        case "loadall":
                            if(words.Length < 3)
                            {
                                sw_out.WriteLine("ERROR: missing override value");
                            }
                            else
                            {
                                bool? replace = words[2] == "true" ? true : words[2] == "falase" ? false : null;
                                if(replace == null)
                                {
                                    sw_out.WriteLine("ERROR: override can be true or false");
                                }
                                else
                                {
                                    var status = db.LoadAll(replace ?? false);
                                    if(status.Status == true)
                                        sw_out.WriteLine($"Load is done: {status.Message}");
                                    else
                                        sw_out.WriteLine($"ERROR: Load is failed: {status.Message}");
                                }
                            }
                            break;
                        case "purge":
                            if(words.Length < 3)
                            {
                                sw_out.WriteLine("ERROR: missing key");
                            }
                            else
                            {
                                var purge = db.Purge(words[2]);
                                if(purge.Status)
                                {
                                    sw_out.WriteLine($"Purge is done: {purge.Message}");
                                }
                                else
                                {
                                    sw_out.WriteLine($"ERROR: Purge is failed: {purge.Message}");
                                }
                            }
                            break;
                        default:
                            sw_out.WriteLine("Action word is missing. Possible actions:");
                            sw_out.WriteLine("Read record:         get <key>");
                            sw_out.WriteLine("List sub records:    getdir <key>");
                            sw_out.WriteLine("List all records:    getall");
                            sw_out.WriteLine("Create record:       set <key> <value>");
                            sw_out.WriteLine("Delete record:       set <key>");
                            sw_out.WriteLine("Delete all records:  delall");
                            sw_out.WriteLine("Delete sub records:  deldir <key>");
                            sw_out.WriteLine("Save record:         save <key>");
                            sw_out.WriteLine("Load record:         load <key> <override: true/false>");
                            sw_out.WriteLine("Load all records:    loadall <override: true/false");
                            sw_out.WriteLine("Purge from file:     purge <key>");
                            break;
                    }

                    sw_out.Close();
                    fs_out.Close();

                    sw.Stop();
                    logger.LogInformation($"Request ({input} was done during {sw.ElapsedMilliseconds}ms");
                }
            }
        }
    }
}
