using BarkFluff.DBEditor.Models;
using BarkFluff.DBEditor.Services;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using System.Collections.ObjectModel;
using System.Windows;

namespace BarkFluff.DBEditor.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly DatabaseService _dbService;

        [ObservableProperty]
        private ObservableCollection<ConfigItem> _configs;

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private bool _hasChanges;

        public MainViewModel(DbCredentials creds)
        {
            _dbService = new DatabaseService(creds);
            Configs = new ObservableCollection<ConfigItem>();
            LoadDataCommand.Execute(null);
        }

        [RelayCommand]
        private async Task LoadDataAsync()
        {
            IsLoading = true;
            try
            {
                var data = await _dbService.GetAllConfigsAsync();
                Configs.Clear();
                foreach (var item in data)
                {
                    // Подписываемся на изменения каждого элемента
                    item.PropertyChanged += Item_PropertyChanged;
                    Configs.Add(item);
                }
                CheckChanges();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load data: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void Item_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ConfigItem.Value))
            {
                CheckChanges();
            }
        }

        private void CheckChanges()
        {
            // Проверяем, есть ли хоть один элемент, где текущее значение != оригинальному
            HasChanges = Configs.Any(c => c.IsChanged);
        }

        [RelayCommand]
        private void Revert()
        {
            foreach (var item in Configs)
            {
                // Просто сбрасываем значение UI обратно в оригинал
                if (item.IsChanged)
                {
                    item.Value = item.OriginalValue;
                }
            }
            CheckChanges();
        }

        [RelayCommand]
        private async Task SaveAsync()
        {
            IsLoading = true;
            try
            {
                var changedItems = Configs.Where(c => c.IsChanged).ToList();

                foreach (var item in changedItems)
                {
                    await _dbService.UpdateConfigAsync(item.Id, item.Value);
                    // Обновляем "оригинал" на новое сохраненное значение
                    item.OriginalValue = item.Value;
                }

                MessageBox.Show("Data successfully saved!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);

                // Можно перезагрузить, чтобы увидеть новые EditedAt даты
                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving data: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }
    }
}
