using Quartz;

namespace MDriveSync.Core
{
    /// <summary>
    /// 基于 Quartz 的作业管理
    /// </summary>
    public class QuartzCronScheduler
    {
        private Timer _timer;  // 定义一个Timer对象，用于定时执行任务
        private string _cronExpression;  // 存储Cron表达式
        private Action _taskToRun;  // 定义一个Action代表要执行的任务

        // 构造函数，接收Cron表达式和一个要执行的任务
        public QuartzCronScheduler(string cronExpression, Action taskToRun)
        {
            _cronExpression = cronExpression;  // 初始化Cron表达式
            _taskToRun = taskToRun;  // 初始化要执行的任务
            _timer = new Timer(TimerCallback);  // 初始化Timer，并设置其回调方法
        }

        // 开始执行调度
        public void Start()
        {
            ScheduleNextRun();  // 调度下一次执行
        }

        // Timer的回调方法
        private void TimerCallback(object state)
        {
            _taskToRun?.Invoke();  // 执行任务
            ScheduleNextRun();  // 重新计划下一次执行
        }

        // 根据Cron表达式计划下一次执行
        private void ScheduleNextRun()
        {
            var schedule = new CronExpression(_cronExpression);  // 解析Cron表达式
            var nextRun = schedule.GetNextValidTimeAfter(DateTimeOffset.Now).GetValueOrDefault();  // 计算下一次执行时间

            var dueTime = nextRun - DateTime.Now;  // 计算距离下一次执行的时间间隔
            if (dueTime.TotalMilliseconds <= 0)
            {
                dueTime = TimeSpan.Zero;  // 如果时间已过，立即执行
            }

            _timer.Change(dueTime, Timeout.InfiniteTimeSpan); // 重设Timer，只执行一次
        }

        // 停止调度器
        public void Stop()
        {
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        // 销毁调度器，释放资源
        public void Dispose()
        {
            _timer.Dispose();
        }
    }
}