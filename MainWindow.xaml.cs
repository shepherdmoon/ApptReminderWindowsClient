using Flurl;
using Flurl.Http;
using Polly;
using Polly.Retry;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Dynamic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;

namespace ApptReminderWindowsClient
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly AsyncRetryPolicy apiPolicy;
        private readonly string SettingsTable = "Settings";

        public MainWindow()
        {
            InitializeComponent();

            // retry all throttling and server error api responses up to 4 times
            var random = new Random();
            bool shouldRetryCall(FlurlHttpException ex)
            {
                int status = (int)ex.Call.Response.StatusCode;
                if (status == 429) return true;
                if (status >= 500 && status < 600) return true;
                return false;
            };
            apiPolicy = Policy
                .Handle<FlurlHttpException>(shouldRetryCall)
                .WaitAndRetryAsync(4, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)).Add(TimeSpan.FromMilliseconds(random.Next(1, 999))));

            // initialize the database
            RunQuery(new SQLiteCommand($"CREATE TABLE IF NOT EXISTS {SettingsTable} (type TEXT NOT NULL PRIMARY KEY, value TEXT)"));
        }

        private dynamic RunQuery(SQLiteCommand command)
        {
            try
            {
                List<Dictionary<string, object>> results = new List<Dictionary<string, object>>();
                using (SQLiteConnection conn = new SQLiteConnection(@"Data Source=local.db;"))
                {
                    conn.Open();
                    command.Connection = conn;
                    SQLiteDataReader reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        Dictionary<string, object> row = new Dictionary<string, object>();
                        for(int index = 0; index < reader.FieldCount; ++index)
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

        private Task<dynamic> AsyncRunQuery(Func<SQLiteCommand> generateCommand)
        {
            SQLiteCommand command = generateCommand();
            return Task.Run(() => RunQuery(command));
        }

        private string GetBaseUrl()
        {
            var result = RunQuery(new SQLiteCommand($"SELECT value FROM {SettingsTable} WHERE type='BaseUrl'"));
            if (result is List<Dictionary<string, object>> rows && rows.Count == 1) return (string)rows[0]["value"];
            return null;
        }

        private string GetApiKey()
        {
            var result = RunQuery(new SQLiteCommand($"SELECT value FROM {SettingsTable} WHERE type='ApiKey'"));
            if (result is List<Dictionary<string, object>> rows && rows.Count == 1) return (string)rows[0]["value"];
            return null;
        }

        private async Task<dynamic> CallApiEndpoint(HttpMethod method, string endpoint, object parameters = null, string baseUrl = null, string apiKey = null)
        {
            if (baseUrl == null) baseUrl = GetBaseUrl();
            if (apiKey == null) apiKey = GetApiKey();
            if (baseUrl == null || apiKey == null) return "Missing url and/or api key";
            try
            {
                return await apiPolicy.ExecuteAsync(
                    delegate()
                    {
                        var url = baseUrl.WithHeader("x-api-key", apiKey).AppendPathSegment(endpoint);
                        if (method == HttpMethod.Get) return url.GetJsonAsync();
                        return url.SendJsonAsync(method, parameters).ReceiveJson();
                    }
                );
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

        /* START SETTINGS TAB FUNCTIONS */
        private async void Settings_Visibility_Listener(object _, DependencyPropertyChangedEventArgs e)
        {
            if (!(bool)e.NewValue) return;

            // get the saved values for url and key
            var url = GetBaseUrl();
            if (url != null) ApiUrl.Value = url;
            var key = GetApiKey();
            if (key != null) ApiKey.Value = key;

            // query for the sms redirect value
            if (url != null && key != null)
            {
                var response = await CallApiEndpoint(HttpMethod.Get, "/org/smsCallRedirect", null, url, key);
                if (response is string)
                {
                    SmsRedirectResponseMessage.Text = response;
                }
                else
                {
                    List<object> smsRedirects = response.numbers;
                    if (smsRedirects.Count > 0) SMSRedirect1.Value = (string)smsRedirects[0];
                    if (smsRedirects.Count > 1) SMSRedirect2.Value = (string)smsRedirects[1];
                    if (smsRedirects.Count > 2) SMSRedirect3.Value = (string)smsRedirects[2];
                    if (smsRedirects.Count > 3) SMSRedirect4.Value = (string)smsRedirects[3];
                    if (smsRedirects.Count > 4) SMSRedirect5.Value = (string)smsRedirects[4];
                }
            }
        }

        private void HealthCheck_Click(object sender, RoutedEventArgs e)
        {
            ApiCallButton button = (ApiCallButton)sender;
            if (button.ErrorMessageBox == null) button.ErrorMessageBox = HealthcheckResponseMessage;
            button.ApiCall = CallApiEndpoint(HttpMethod.Get, "/healthcheck", null, ApiUrl.Value, ApiKey.Value);
        }

        private void SaveApiSettings_Click(object sender, RoutedEventArgs e)
        {
            ApiCallButton button = (ApiCallButton)sender;
            if (button.ErrorMessageBox == null) button.ErrorMessageBox = HealthcheckResponseMessage;
            button.ApiCall = AsyncRunQuery(() =>
            {
                SQLiteCommand insert = new SQLiteCommand($"INSERT INTO {SettingsTable} (type, value) VALUES(@urlType, @urlValue),(@keyType, @keyValue)");
                insert.Parameters.AddWithValue("@urlType", "BaseUrl");
                insert.Parameters.AddWithValue("@urlValue", ApiUrl.Value);
                insert.Parameters.AddWithValue("@keyType", "ApiKey");
                insert.Parameters.AddWithValue("@keyValue", ApiKey.Value);
                return insert;
            });
        }

        private void SmsRedirect_Click(object sender, RoutedEventArgs e)
        {
            ApiCallButton button = (ApiCallButton)sender;
            if (button.ErrorMessageBox == null) button.ErrorMessageBox = SmsRedirectResponseMessage;
            List<string> numbers = new List<string>();
            if (SMSRedirect1.Value != "") numbers.Add(SMSRedirect1.Value);
            if (SMSRedirect2.Value != "") numbers.Add(SMSRedirect2.Value);
            if (SMSRedirect3.Value != "") numbers.Add(SMSRedirect3.Value);
            if (SMSRedirect4.Value != "") numbers.Add(SMSRedirect4.Value);
            if (SMSRedirect5.Value != "") numbers.Add(SMSRedirect5.Value);
            dynamic parameters = new ExpandoObject();
            if (numbers.Count != 0) parameters.numbers = numbers;
            button.ApiCall = CallApiEndpoint(HttpMethod.Put, "/org/smsCallRedirect", parameters);
        }
        /* END SETTINGS TAB FUNCTIONS */
    }
}
