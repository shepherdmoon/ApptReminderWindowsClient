using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Dynamic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace ApptReminderWindowsClient
{
    /// <summary>
    /// Interaction logic for TemplatesTab.xaml
    /// </summary>
    public partial class TemplatesTab : UserControl
    {
        public TemplatesTab()
        {
            InitializeComponent();
        }

        public Helper Utils
        {
            get => (Helper)GetValue(UtilsProperty);
            set => SetValue(UtilsProperty, value);
        }
        public static readonly DependencyProperty UtilsProperty = DependencyProperty.Register("Utils", typeof(Helper), typeof(TemplatesTab), new UIPropertyMetadata(null));

        public Window Owner
        {
            get => (Window)GetValue(OwnerProperty);
            set => SetValue(OwnerProperty, value);
        }
        public static readonly DependencyProperty OwnerProperty = DependencyProperty.Register("Owner", typeof(Window), typeof(TemplatesTab), new UIPropertyMetadata(null));

        private readonly string AddNew = "Add New";
        private readonly Dictionary<string, List<dynamic>> templates = new Dictionary<string, List<dynamic>>();
        private void UpdateTemplateNameSelectorItems()
        {
            List<KeyValuePair<string, string>> templateNames = new List<KeyValuePair<string, string>>();
            foreach (string name in templates.Keys)
            {
                string value = name;
                var result = Utils.RunQuery(new SQLiteCommand($"SELECT value FROM {Helper.TemplateNameTable} WHERE name='{name}'"));
                if (result is List<Dictionary<string, object>> rows && rows.Count == 1) value = (string)rows[0]["value"];
                templateNames.Add(new KeyValuePair<string, string>(name, value));
            }
            if (templateNames.Count == 0)
            {
                string value = Utils.RunQuery(new SQLiteCommand($"SELECT value FROM {Helper.TemplateNameTable} WHERE name='{Helper.DefaultTemplateName}'"))[0]["value"];
                templateNames.Add(new KeyValuePair<string, string>(Helper.DefaultTemplateName, value));
            }
            templateNames.Sort((p1, p2) =>
            {
                if (p1.Key == Helper.DefaultTemplateName) return -1;
                if (p2.Key == Helper.DefaultTemplateName) return 1;
                return p1.Value.CompareTo(p2.Value);
            });
            templateNames.Add(new KeyValuePair<string, string>(null, AddNew));
            TemplateNameSelector.ItemsSource = templateNames;
        }

        private void UpdateTemplateLanguageSelectorItems()
        {
            HashSet<string> languages = new HashSet<string>();
            foreach (dynamic data in templates[(string)TemplateNameSelector.SelectedValue])
            {
                if (data.language != null) languages.Add((string)data.language);
            }
            List<KeyValuePair<string, string>> templateLanguages = new List<KeyValuePair<string, string>>();
            foreach (string language in languages)
            {
                templateLanguages.Add(new KeyValuePair<string, string>(language, language));
            }
            templateLanguages.Sort((p1, p2) => p1.Value.CompareTo(p2.Value));
            templateLanguages.Insert(0, new KeyValuePair<string, string>(Helper.DefaultTemplateName, ""));
            templateLanguages.Add(new KeyValuePair<string, string>(null, AddNew));
            TemplateLanguageSelector.ItemsSource = templateLanguages;
        }

        private async void Templates_Visibility_Listener(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!(bool)e.NewValue) return;
            List<UIElement> elements = Helper.GetAllElements((Grid)sender, new Type[] { typeof(ApiCallButton), typeof(ComboBox) });
            elements.ForEach(element => element.IsEnabled = false);

            // get the existing templates
            templates.Clear();
            dynamic response = await Utils.CallApiEndpoint(HttpMethod.Get, "/template");
            if (response is string)
            {
                TemplateResponseMessage.Text = response;
            }
            else
            {
                // sort all the templates by name
                foreach (dynamic template in (dynamic[])response)
                {
                    string name = template.name ?? Helper.DefaultTemplateName;
                    if (!templates.ContainsKey(name)) templates.Add(name, new List<dynamic>());
                    templates[name].Add(template);
                }

                // update the template combo box
                string curTemplateName = (string)TemplateNameSelector.SelectedValue;
                UpdateTemplateNameSelectorItems();
                TemplateNameSelector.SelectedIndex = -1;
                if (curTemplateName == null || !templates.ContainsKey(curTemplateName)) TemplateNameSelector.SelectedIndex = 0;
                else TemplateNameSelector.SelectedValue = curTemplateName;
            }
            elements.ForEach(element => element.IsEnabled = true);
        }

        private void TemplateNameSelector_SelectionChanged(object _, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count == 0) return;
            string name = (string)TemplateNameSelector.SelectedValue;
            if (name == null)
            {
                AddItemDialog dialog = new AddItemDialog
                {
                    Owner = Owner,
                    Validation = value =>
                    {
                        if (value == null || value == "") return false;
                        if (value.Length >= 256) return false;
                        if (value.IndexOf("|") != -1) return false;
                        if (templates.ContainsKey(value)) return false;
                        return true;
                    }
                };
                dialog.ShowDialog();
                if (dialog.Value == null)
                {
                    TemplateNameSelector.SelectedItem = e.RemovedItems[0];
                }
                else
                {
                    List<dynamic> values = new List<dynamic>
                    {
                        new { type = "Email", title = "", message = "", subtype = (string)null, language = (string)null },
                        new { type = "Text", message = "", subtype = (string)null, language = (string)null }
                    };
                    templates.Add(dialog.Value, values);
                    UpdateTemplateNameSelectorItems();
                    TemplateNameSelector.SelectedValue = dialog.Value;
                }
                return;
            }
            TextMessage.Value = "";
            EmailTitle.Value = "";
            EmailBody.Value = "";
            foreach (dynamic data in templates[name])
            {
                if (data.language != null) continue;
                if (data.type == "Text")
                {
                    TextMessage.Value = data.message ?? "";
                }
                if (data.type == "Email")
                {
                    EmailTitle.Value = data.title ?? "";
                    EmailBody.Value = data.message ?? "";
                }
            }
            string curTemplateLang = (string)TemplateLanguageSelector.SelectedValue;
            UpdateTemplateLanguageSelectorItems();
            TemplateLanguageSelector.SelectedIndex = -1;
            if (curTemplateLang == null || templates[name].FindIndex(data => data.language == curTemplateLang) == -1) TemplateLanguageSelector.SelectedIndex = 0;
            else TemplateLanguageSelector.SelectedValue = curTemplateLang;
        }

        private void TemplateLanguageSelector_SelectionChanged(object _, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count == 0) return;
            string language = (string)TemplateLanguageSelector.SelectedValue;
            if (language == null)
            {
                AddItemDialog dialog = new AddItemDialog
                {
                    Owner = Owner,
                    Validation = value =>
                    {
                        if (value == null || value == "") return false;
                        if (value.Length >= 128) return false;
                        if (value.IndexOf("|") != -1) return false;
                        if (templates[(string)TemplateNameSelector.SelectedValue].FindIndex(data => data.language == value) != -1) return false;
                        return true;
                    }
                };
                dialog.ShowDialog();
                if (dialog.Value == null)
                {
                    TemplateLanguageSelector.SelectedItem = e.RemovedItems[0];
                }
                else
                {
                    List<dynamic> values = templates[(string)TemplateNameSelector.SelectedValue];
                    values.AddRange(new dynamic[] {
                        new { type = "Email", title = "", message = "", subtype = (string)null, language = dialog.Value },
                        new { type = "Text", message = "", subtype = (string)null, language = dialog.Value }
                    });
                    UpdateTemplateLanguageSelectorItems();
                    TemplateLanguageSelector.SelectedValue = dialog.Value;
                }
                return;
            }
            TextMessageLang.IsEnabled = language != Helper.DefaultTemplateName;
            TextMessageLang.Value = "";
            EmailTitleLang.IsEnabled = language != Helper.DefaultTemplateName;
            EmailTitleLang.Value = "";
            EmailBodyLang.IsEnabled = language != Helper.DefaultTemplateName;
            EmailBodyLang.Value = "";
            foreach (dynamic data in templates[(string)TemplateNameSelector.SelectedValue])
            {
                if (data.language != language) continue;
                if (data.type == "Text")
                {
                    TextMessageLang.Value = data.message ?? "";
                }
                if (data.type == "Email")
                {
                    EmailTitleLang.Value = data.title ?? "";
                    EmailBodyLang.Value = data.message ?? "";
                }
            }
        }

        private string FormatEmailBody(string text)
        {
            if (text == null) text = "";
            Regex re = new Regex(@"^\s*<html>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            if (!re.IsMatch(text)) text = $"<html>\n\t<head></head>\n\t<body>\n\t\t{text}\n\t</body>\n</html>";
            return text;
        }

        private dynamic GenerateParams(string type, string name = null, string language = null, string subtype = null, string title = null, string message = null)
        {
            dynamic value = new ExpandoObject();
            value.type = type;
            if (name != null && name != "") value.name = name;
            if (language != null && language != "") value.language = language;
            if (subtype != null && subtype != "") value.subtype = subtype;
            if (title != null && title != "") value.title = title;
            if (message != null && message != "") value.message = message;
            return value;
        }

        private void SaveTemplates_Click(object sender, RoutedEventArgs _)
        {
            ApiCallButton button = (ApiCallButton)sender;
            if (button.ErrorMessageBox == null) button.ErrorMessageBox = TemplateResponseMessage;
            List<Task<dynamic>> apiCalls = new List<Task<dynamic>>();
            string name = (string)TemplateNameSelector.SelectedValue;
            if (name == Helper.DefaultTemplateName) name = null;
            if (TextMessage.Value == "") apiCalls.Add(Utils.CallApiEndpoint(HttpMethod.Delete, "/template", GenerateParams("Text", name)));
            else apiCalls.Add(Utils.CallApiEndpoint(HttpMethod.Put, "/template", GenerateParams("Text", name, message: TextMessage.Value)));
            if (EmailTitle.Value == "" && EmailBody.Value == "") apiCalls.Add(Utils.CallApiEndpoint(HttpMethod.Delete, "/template", GenerateParams("Email", name)));
            else apiCalls.Add(Utils.CallApiEndpoint(HttpMethod.Put, "/template", GenerateParams("Email", name, title: EmailTitle.Value, message: FormatEmailBody(EmailBody.Value))));
            if (TemplateLanguageSelector.SelectedIndex != 0)
            {
                string language = (string)TemplateLanguageSelector.SelectedValue;
                if (TextMessageLang.Value == "") apiCalls.Add(Utils.CallApiEndpoint(HttpMethod.Delete, "/template", GenerateParams("Text", name, language)));
                else apiCalls.Add(Utils.CallApiEndpoint(HttpMethod.Put, "/template", GenerateParams("Text", name, language, message: TextMessageLang.Value)));
                if (EmailTitleLang.Value == "" && EmailBodyLang.Value == "") apiCalls.Add(Utils.CallApiEndpoint(HttpMethod.Delete, "/template", GenerateParams("Email", name, language)));
                else apiCalls.Add(Utils.CallApiEndpoint(HttpMethod.Put, "/template", GenerateParams("Email", name, language, title: EmailTitleLang.Value, message: FormatEmailBody(EmailBodyLang.Value))));
            }
            button.SetApiCalls(apiCalls);
        }
    }
}
