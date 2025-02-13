using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace WpfApp4
{
    /// <summary>
    /// Логика взаимодействия для MainWindow.xaml
    /// </summary>
    public enum ThreadStatus
    {
        Initialized,
        Pending,
        InProgress,
        Terminated
    }

    public class TaskThread : INotifyPropertyChanged
    {
        public int TaskId { get; }
        private ThreadStatus threadStatus;
        public ThreadStatus Status
        {
            get { return threadStatus; }
            set { threadStatus = value; OnPropertyChanged("Status"); OnPropertyChanged("StatusInfo"); }
        }

        private int taskCounter;
        public int Counter
        {
            get { return taskCounter; }
            set { taskCounter = value; OnPropertyChanged("Counter"); OnPropertyChanged("StatusInfo"); }
        }
        public DateTime TaskStartTime { get; set; }
        public CancellationTokenSource CancellationToken { get; }
        public bool IsForcedStop { get; set; }
        public string StatusInfo
        {
            get
            {
                return $"Задача {TaskId} –> Счётчик: {Counter} –> {Status}";
            }
        }
        public TaskThread(int taskId)
        {
            TaskId = taskId;
            Status = ThreadStatus.Initialized;
            Counter = 0;
            CancellationToken = new CancellationTokenSource();
            IsForcedStop = false;
        }
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public partial class MainWindow : Window
    {
        private int nextTaskId = 1;
        private int maxConcurrentTasks;
        private SemaphoreSlim threadSemaphore;
        private ObservableCollection<TaskThread> initializedThreads = new ObservableCollection<TaskThread>();
        private ObservableCollection<TaskThread> pendingThreads = new ObservableCollection<TaskThread>();
        private ObservableCollection<TaskThread> activeThreads = new ObservableCollection<TaskThread>();
       
        public MainWindow()
        {
            InitializeComponent();

            lbInitialized.ItemsSource = initializedThreads;
            lbPending.ItemsSource = pendingThreads;
            lbActive.ItemsSource = activeThreads;

            if (int.TryParse(tbMaxConcurrent.Text, out int max))
            {
                maxConcurrentTasks = max;
            }
            else
            {
                maxConcurrentTasks = 3;
            }
            threadSemaphore = new SemaphoreSlim(maxConcurrentTasks, int.MaxValue);
        }

        private void btnCreateThread_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                TaskThread task = new TaskThread(nextTaskId++);
                initializedThreads.Add(task);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void lbCreated_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (lbInitialized.SelectedItem is TaskThread task)
                {
                    initializedThreads.Remove(task);
                    task.Status = ThreadStatus.Pending;
                    pendingThreads.Add(task);

                    StartTaskFromPending(task);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void lbWorking_MouseDoubleClick(object sender, MouseButtonEventArgs e) 
        {
            try
            {
                if (lbActive.SelectedItem is TaskThread task)
                    StopTask(task, false);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private async void StartTaskFromPending(TaskThread task)
        {
            try
            {
                await threadSemaphore.WaitAsync(task.CancellationToken.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            Dispatcher.Invoke(() =>
            {
                if (pendingThreads.Contains(task))
                {
                    pendingThreads.Remove(task);
                    task.Status = ThreadStatus.InProgress;
                    task.TaskStartTime = DateTime.Now;
                    activeThreads.Add(task);
                }
            });

            RunTaskLoop(task);
        }

        private async void RunTaskLoop(TaskThread task)
        {
            try
            {
                while (!task.CancellationToken.Token.IsCancellationRequested)
                {
                    await Task.Delay(1000, task.CancellationToken.Token);
                    Dispatcher.Invoke(() =>
                    {
                        task.Counter++;
                    });
                }
            }
            catch (TaskCanceledException) { }
            finally
            {
                if (!task.IsForcedStop)
                    threadSemaphore.Release();

                Dispatcher.Invoke(() =>
                {
                    activeThreads.Remove(task);
                    task.Status = ThreadStatus.Terminated;
                });
            }
        }

        private void StopTask(TaskThread task, bool forced)
        {
            if (task != null)
            {
                task.IsForcedStop = forced;
                task.CancellationToken.Cancel();
            }
        }

        private void btnUpdateMax_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (int.TryParse(tbMaxConcurrent.Text, out int newMax))
                {
                    int oldMax = maxConcurrentTasks;
                    maxConcurrentTasks = newMax;

                    if (newMax > oldMax)
                    {
                        int diff = newMax - oldMax;
                        threadSemaphore.Release(diff);
                    }
                    else if (newMax < oldMax)
                    {
                        int excess = activeThreads.Count - newMax;
                        if (excess > 0)
                        {
                            var toStop = new System.Collections.Generic.List<TaskThread>(activeThreads);
                            toStop.Sort((t1, t2) => t1.TaskStartTime.CompareTo(t2.TaskStartTime));
                            for (int i = 0; i < excess; i++)
                            {
                                StopTask(toStop[i], true);
                            }
                        }
                    }
                }
                else
                {
                    MessageBox.Show("Некорректное значение лимита");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }
    }
}
