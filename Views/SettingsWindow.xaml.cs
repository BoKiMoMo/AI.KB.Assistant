using System;
using System.Windows;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly string _configPath;
        private AppConfig _cfg = new();

        public SettingsWindow(string configPath)
        {
            InitializeComponent();
            _configPath = configPath;
            _cfg = AppConfig.Load(_configPath);
            Bind();
        }

        private void Bind()
        {
            TxtRoot.Text = _cfg.RootPath;
            TxtProject.Text = _cfg.Project;
            TxtRoutingMode.Text = _cfg.RoutingMode;
            ChkYear.IsChecked = _cfg.AddYearFolder;

            TxtApiKey.Password = _cfg.OpenAI.ApiKey ?? "";
            TxtModel.Text = _cfg.OpenAI.Model ?? "";
            TxtEmb.Text = _cfg.OpenAI.EmbeddingModel ?? "";
            TxtTemp.Text = _cfg.OpenAI.Temperature.ToString();
            TxtMaxTok.Text = _cfg.OpenAI.MaxTokens.ToString();

            ChkAuto.IsChecked = _cfg.AutoClassify;
            TxtConf.Text = _cfg.ConfidenceThreshold.ToString("0.##");

            ChkSemantic.IsChecked = _cfg.Semantic.Enabled;
            TxtTopK.Text = _cfg.Semantic.TopK.ToString();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _cfg.RootPath = TxtRoot.Text.Trim();
                _cfg.Project = TxtProject.Text.Trim();
                _cfg.RoutingMode = TxtRoutingMode.Text.Trim();
                _cfg.AddYearFolder = ChkYear.IsChecked ?? true;

                _cfg.OpenAI.ApiKey = TxtApiKey.Password.Trim();
                _cfg.OpenAI.Model = TxtModel.Text.Trim();
                _cfg.OpenAI.EmbeddingModel = TxtEmb.Text.Trim();
                _cfg.OpenAI.Temperature = double.TryParse(TxtTemp.Text, out var t) ? t : 0.2;
                _cfg.OpenAI.MaxTokens = int.TryParse(TxtMaxTok.Text, out var mt) ? mt : 500;

                _cfg.AutoClassify = ChkAuto.IsChecked ?? false;
                _cfg.ConfidenceThreshold = double.TryParse(TxtConf.Text, out var c) ? c : 0.6;

                _cfg.Semantic.Enabled = (ChkSemantic.IsChecked ?? true);
                _cfg.Semantic.TopK = int.TryParse(TxtTopK.Text, out var k) ? k : 1000;

                _cfg.Save(_configPath);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"儲存失敗：{ex.Message}");
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
