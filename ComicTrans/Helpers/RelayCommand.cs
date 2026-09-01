using System;
using System.Windows.Input;

namespace ComicTrans.Helpers
{
    /// <summary>
    /// Triển khai giao diện ICommand cho các lệnh không tham số.
    /// Giúp liên kết các nút bấm hoặc sự kiện từ XAML đến các phương thức trong ViewModel.
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute == null || _canExecute();

        public void Execute(object? parameter) => _execute();

        // Sự kiện thông báo trạng thái CanExecute thay đổi để UI cập nhật trạng thái Enable/Disable nút bấm
        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }

    /// <summary>
    /// Triển khai giao diện ICommand cho các lệnh có tham số kiểu T.
    /// </summary>
    /// <typeparam name="T">Kiểu dữ liệu của tham số truyền vào lệnh</typeparam>
    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T?> _execute;
        private readonly Func<T?, bool>? _canExecute;

        public RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter)
        {
            if (_canExecute == null) return true;
            
            if (parameter == null && typeof(T).IsValueType) return false;
            
            return _canExecute((T?)parameter);
        }

        public void Execute(object? parameter) => _execute((T?)parameter);

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}
