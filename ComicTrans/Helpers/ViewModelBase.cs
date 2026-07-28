using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.Generic;

namespace ComicTrans.Helpers
{
    /// <summary>
    /// Lớp cơ sở cho tất cả các ViewModel trong ứng dụng.
    /// Triển khai INotifyPropertyChanged để thông báo cho giao diện (UI) cập nhật tự động khi thuộc tính thay đổi.
    /// </summary>
    public class ViewModelBase : INotifyPropertyChanged
    {
        // Sự kiện xảy ra khi thuộc tính thay đổi giá trị
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Phát ra sự kiện thay đổi thuộc tính.
        /// </summary>
        /// <param name="propertyName">Tên thuộc tính thay đổi (tự động lấy tên thuộc tính gọi hàm này)</param>
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Gán giá trị mới cho một trường (field) và phát sự kiện OnPropertyChanged nếu giá trị thay đổi.
        /// </summary>
        /// <typeparam name="T">Kiểu dữ liệu của thuộc tính</typeparam>
        /// <param name="field">Tham chiếu tới trường lưu giá trị</param>
        /// <param name="value">Giá trị mới cần gán</param>
        /// <param name="propertyName">Tên thuộc tính</param>
        /// <returns>True nếu giá trị thực sự thay đổi, ngược lại là False</returns>
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            // Nếu giá trị mới trùng với giá trị cũ thì không làm gì cả
            if (EqualityComparer<T>.Default.Equals(field, value)) 
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
