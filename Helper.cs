using Flurl.Http;
using Flurl.Http.Configuration;
using Newtonsoft.Json;
using Polly;
using Polly.Retry;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace ApptReminderWindowsClient
{
    public class Helper
    {
        public static readonly string DateStringFormat = "MMM d yyyy h:mm tt";
        public static readonly string SettingsTable = "Settings";
        public static readonly string TemplateNameTable = "TemplateNames";
        public static readonly string AppointmentTable = "Appointments";
        public static readonly string ApiUrlType = "BaseUrl";
        public static readonly string ApiKeyType = "ApiKey";
        // use | as the default because that is not a valid name so it will never conflict with a user selected name
        public static readonly string DefaultTemplateName = "|";

        public static List<UIElement> GetAllElements(Panel container, Type[] types)
        {
            List<UIElement> elements = new List<UIElement>();
            foreach (UIElement control in container.Children)
            {
                if (control.GetType().IsSubclassOf(typeof(Panel))) elements.AddRange(GetAllElements((Panel)control, types));
                if (types.Contains(control.GetType())) elements.Add(control);
            }
            return elements;
        }

        public AsyncRetryPolicy ApiPolicy { get; private set; }

        public Helper()
        {
            // don't convert any date like strings to dates - https://github.com/JamesNK/Newtonsoft.Json/issues/862
            FlurlHttp.Configure(settings =>
            {
                var jsonSettings = new JsonSerializerSettings
                {
                    DateParseHandling = DateParseHandling.None
                };
                settings.JsonSerializer = new NewtonsoftJsonSerializer(jsonSettings);
            });

            // retry all throttling and server error api responses up to 4 times
            var random = new Random();
            bool shouldRetryCall(FlurlHttpException ex)
            {
                int status = (int)ex.Call.Response.StatusCode;
                if (status == 429) return true;
                if (status >= 500 && status < 600) return true;
                return false;
            };
            ApiPolicy = Policy
                .Handle<FlurlHttpException>(shouldRetryCall)
                .WaitAndRetryAsync(4, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)).Add(TimeSpan.FromMilliseconds(random.Next(1, 999))));

            // initialize the database
            RunQuery(new SQLiteCommand($"CREATE TABLE IF NOT EXISTS {SettingsTable} (type TEXT NOT NULL PRIMARY KEY, value TEXT)"));
            RunQuery(new SQLiteCommand($"CREATE TABLE IF NOT EXISTS {TemplateNameTable} (name TEXT NOT NULL PRIMARY KEY, value TEXT)"));
            RunQuery(new SQLiteCommand($"INSERT OR IGNORE INTO {TemplateNameTable} (name, value) VALUES ('{DefaultTemplateName}', 'Default')"));
            RunQuery(new SQLiteCommand($"CREATE TABLE IF NOT EXISTS {AppointmentTable} (id TEXT NOT NULL, name TEXT, date INTEGER, date_format TEXT, PRIMARY KEY (id, date))"));
        }

        public dynamic RunQuery(SQLiteCommand command, string dataSource = "local")
        {
            try
            {
                List<Dictionary<string, object>> results = new List<Dictionary<string, object>>();
                using (SQLiteConnection conn = new SQLiteConnection($@"Data Source={dataSource}.db;"))
                {
                    conn.Open();
                    command.Connection = conn;
                    SQLiteDataReader reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        Dictionary<string, object> row = new Dictionary<string, object>();
                        for (int index = 0; index < reader.FieldCount; ++index)
                        {
                            row[reader.GetName(index)] = reader.GetValue(index);
                        }
                        results.Add(row);
                    }
                    reader.Close();
                }
                return results;
            }
            catch (Exception e)
            {
                return e.Message;
            }
            finally
            {
                command.Dispose();
            }
        }

        public Task<dynamic> AsyncRunQuery(Func<SQLiteCommand> generateCommand, string dataSource = "local")
        {
            SQLiteCommand command = generateCommand();
            return Task.Run(() => RunQuery(command, dataSource));
        }

        public string GetBaseUrl()
        {
            var result = RunQuery(new SQLiteCommand($"SELECT value FROM {SettingsTable} WHERE type='{ApiUrlType}'"));
            if (result is List<Dictionary<string, object>> rows && rows.Count == 1) return (string)rows[0]["value"];
            return null;
        }

        public string GetApiKey()
        {
            var result = RunQuery(new SQLiteCommand($"SELECT value FROM {SettingsTable} WHERE type='{ApiKeyType}'"));
            if (result is List<Dictionary<string, object>> rows && rows.Count == 1) return (string)rows[0]["value"];
            return null;
        }

        public async Task<dynamic> CallApiEndpoint(HttpMethod method, string endpoint, object parameters = null, IDictionary<string, object> queryParams = null, string baseUrl = null, string apiKey = null)
        {
            if (baseUrl == null) baseUrl = GetBaseUrl();
            if (apiKey == null) apiKey = GetApiKey();
            if (baseUrl == null || apiKey == null) return "Missing url and/or api key";
            try
            {
                return await ApiPolicy.ExecuteAsync(
                    delegate ()
                    {
                        var url = baseUrl.WithHeader("x-api-key", apiKey).AppendPathSegment(endpoint);
                        if (queryParams != null) url = url.SetQueryParams(queryParams);
                        if (method == HttpMethod.Get) return url.GetJsonAsync();
                        return url.SendJsonAsync(method, parameters).ReceiveJson();
                    }
                );
            }
            catch (FlurlParsingException ex) when (ex.Call.Response.IsSuccessStatusCode)
            {
                // this is to handle responses that are an array
                return await ex.GetResponseJsonAsync<dynamic[]>();
            }
            catch (FlurlHttpException ex)
            {
                return ex.Call.Exception.Message;
            }
            catch (Exception e)
            {
                return e.Message;
            }
        }
    }
}
