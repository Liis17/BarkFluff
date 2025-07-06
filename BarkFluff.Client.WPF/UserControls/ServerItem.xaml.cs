using BarkFluff.WebApi.Core.MessengerData;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;

namespace BarkFluff.Client.WPF.UserControls
{
    /// <summary>
    /// Логика взаимодействия для ServerItem.xaml
    /// </summary>
    public partial class ServerItem : UserControl
    {
        private string _ip = string.Empty;
        public ServerItem(ServerDataElement serverData)
        {
            InitializeComponent();
            ServerTitle.Text = serverData.Title;
            ServerDescription.Text = serverData.Description;
            ServerInfo.Text = $@"{serverData.Ip} • {serverData.UserCount}";
            _ip = serverData.Ip;
        }

        private void PublicServer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                App.GParam.SocketBeacon = _ip;
                App.ServerCommunication.CreateOnlyBeaconAC(App.GParam);

                try
                {
                    App.ServerCommunication.GetServerInfo(App.GParam);
                    GlobalParam.Save(App.GParam, Path.Combine(App.GParam.AppPath, "GlobalParam.json"), App.GParam.AppPass);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка подключения: {ex.Message}");
                    return;
                }
                App.MessengerWindow.OpenLoginPage();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Неизвестная ошибка: {ex.Message}");
            }
        }
    }
}
