using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DeskWidgets.Models;
using DeskWidgets.Services;

namespace DeskWidgets.Widgets
{
    public partial class NotesWidget : WidgetBase
    {
        public override string Id => "notes";
        public override string DisplayName => "Notas";
        public override string Glyph => "";
        public override Size BaseSize => new Size(210, 230);
        public override Size MinSize => new Size(150, 120);

        public bool IsEditing => IsKeyboardFocusWithin;

        public NotesWidget()
        {
            InitializeComponent();
            NotesList.ItemsSource = SettingsService.Notes;
            SettingsService.NotesChanged += UpdateSummary;
            IsKeyboardFocusWithinChanged += (s, e) =>
            {
                if (IsKeyboardFocusWithin) TodoistSync.SyncNow(); // pega o que você escreveu no celular
                else RemoveEmpty();
            };
            UpdateSummary();
        }

        public override void ApplySettings()
        {
            bool todoist = S.NotesSource == "todoist" && !string.IsNullOrEmpty(S.TodoistProjectId);
            SyncIcon.Visibility = todoist ? Visibility.Visible : Visibility.Collapsed;
            NotesTitle.Text = todoist ? S.TodoistProjectName : "Notas";
            UpdateSummary();
        }

        public override IEnumerable<MenuItem> MenuItems()
        {
            yield return Item("Novo item", (s, e) => FocusNew());
            yield return Item("Limpar concluídos", ClearDone_Click);
        }

        public void FocusNew()
        {
            Host?.BringToFront();
            NewBox.Focus();
            Keyboard.Focus(NewBox);
        }

        private void UpdateSummary()
        {
            int total = SettingsService.Notes.Count;
            int done = SettingsService.Notes.Count(n => n.Done);
            NotesCount.Text = total == 0 ? "" : done + "/" + total;
            NotesEmpty.Visibility = total == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RemoveEmpty()
        {
            foreach (var n in SettingsService.Notes.Where(n => string.IsNullOrWhiteSpace(n.Text)).ToList())
                SettingsService.Notes.Remove(n);
        }

        private void ClearDone_Click(object sender, RoutedEventArgs e)
        {
            foreach (var n in SettingsService.Notes.Where(n => n.Done).ToList())
                SettingsService.Notes.Remove(n);
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is NoteItem item)
                SettingsService.Notes.Remove(item);
        }

        private void NewBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                string t = NewBox.Text.Trim();
                if (t.Length > 0) SettingsService.Notes.Add(new NoteItem { Text = t });
                NewBox.Clear();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                NewBox.Clear();
                Keyboard.ClearFocus();
                e.Handled = true;
            }
        }

        private void NewBox_TextChanged(object sender, TextChangedEventArgs e)
            => NewPlaceholder.Visibility = NewBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
