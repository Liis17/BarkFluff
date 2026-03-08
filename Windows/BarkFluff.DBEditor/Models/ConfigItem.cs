using CommunityToolkit.Mvvm.ComponentModel;
namespace BarkFluff.DBEditor.Models
{
    public partial class ConfigItem : ObservableObject
    {
        public int Id { get; set; }
        public string Section { get; set; }
        public string Key { get; set; }

        // Поле, которое мы меняем
        [ObservableProperty]
        private string _value;

        public DateTime EditedAt { get; set; }
        public string EditedBy { get; set; }
        public string EditedFrom { get; set; }
        public int ServiceId { get; set; }

        // Храним оригинальное значение, чтобы знать, изменилось ли оно
        public string OriginalValue { get; set; }

        // Свойство, показывающее, отличается ли текущее значение от исходного
        public bool IsChanged => Value != OriginalValue;
    }
}
