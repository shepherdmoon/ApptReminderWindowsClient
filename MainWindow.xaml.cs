using Flurl;
using Flurl.Http;
using Polly;
using Polly.Retry;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace ApptReminderWindowsClient
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly AsyncRetryPolicy apiPolicy;

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
        }

        private List<object[]> RunQuery(SQLiteCommand command)
        {
            List<object[]> results = new List<object[]>();
            using (SQLiteConnection conn = new SQLiteConnection(@"Data Source=local.db;"))
            {
                conn.Open();
                command.Connection = conn;
                SQLiteDataReader reader = command.ExecuteReader();
                int numCols = reader.GetSchemaTable().Columns.Count;
                while (reader.Read())
                {
                    object[] row = new object[numCols];
                    reader.GetValues(row);
                    results.Add(row);
                }
                reader.Close();
            }
            return results;
        }

        private async Task<dynamic> CallApiEndpoint(HttpMethod method, string endpoint, object parameters = null, string baseUrl = null, string apiKey = null)
        {
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
        
        // settings tab functions
        private void HealthCheck_Click(object sender, RoutedEventArgs e)
        {
            ApiCallButton button = (ApiCallButton)sender;
            if (button.ErrorMessageBox == null) button.ErrorMessageBox = HealthcheckResponseMessage;
            button.ApiCall = CallApiEndpoint(HttpMethod.Get, "/healthcheck", null, ApiUrl.Value, ApiKey.Value);
        }

        private void SaveApiSettings_Click(object sender, RoutedEventArgs e)
        {
            SQLiteCommand saveApiUrl = new SQLiteCommand();
            saveApiUrl.CommandText = "INSERT INTO Settings (type, value) VALUES(@type, @value)";
            saveApiUrl.Parameters.Add(new SQLiteParameter("@type", "BaseUrl"));
            saveApiUrl.Parameters.Add(new SQLiteParameter("@value", ApiUrl.Value));
            RunQuery(saveApiUrl);
            SQLiteCommand saveApiKey = new SQLiteCommand();
            saveApiKey.CommandText = "INSERT INTO Settings (type, value) VALUES(@type, @value)";
            saveApiKey.Parameters.Add(new SQLiteParameter("@type", "ApiKey"));
            saveApiKey.Parameters.Add(new SQLiteParameter("@value", ApiKey.Value));
            RunQuery(saveApiKey);
        }
    }
}
