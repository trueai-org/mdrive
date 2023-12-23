using MDriveSync.Core.Services;
using RestSharp;
using System.Security.Policy;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace MDriveSync.WPF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            //InitializeAsync();

            // 订阅消息事件
            App.MessageReceived += App_MessageReceived;

            SelectionChanged.ItemsSource = new ItemsForSale();

            var taskList = new List<Task>();

            taskList.Add(new Task("阿里云盘", "阿里云盘", 1, TaskType.Work));
            taskList.Add(new Task("Groceries11", "Pick up Groceries and Detergent", 2, TaskType.Home));
            taskList.Add(new Task("Laundry22", "Do my Laundry", 2, TaskType.Home));
            taskList.Add(new Task("Clean", "Clean my office", 3, TaskType.Work));
            taskList.Add(new Task("Dinner", "Get ready for family reunion", 2, TaskType.Home));
            taskList.Add(new Task("Proposals", "Review new budget proposals", 2, TaskType.Work));

            jobListBox.ItemsSource = taskList;
        }

        public List<string> LogMessages = new List<string>();

        private void App_MessageReceived(object sender, string message)
        {
            // 在这里处理接收到的消息，例如更新 UI
            Dispatcher.Invoke(() =>
            {
                // 更新UI的代码，比如将消息添加到列表中
                //LogMessages.Add(message);

                //txtLog.Text += message + "\r\n";
                txtInfo.Text += message + "\r\n";
            });
        }

        private void OnImagesDirChangeClick(object sender, RoutedEventArgs e)
        {
            var jobs = App.TimedHostedService.GetJobs();
            var auth = ProviderApiHelper.GetAuthQrcode();

            var pvWindow = new PhotoViewer { SelectedPhoto = new Photo(auth.QrCodeUrl) };
            pvWindow.Owner = this; // 'this' 指当前活动的窗口
            pvWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            pvWindow.Show();
        }

        private void itemsControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }
    }
}