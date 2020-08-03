using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Shapes;
using MessageBox = System.Windows.MessageBox;
using Path = System.IO.Path;
using static BatchRenamerGUI.AssertionFailure;

namespace BatchRenamerGUI
{ 
    public partial class MainWindow : Window
    {
        static Brush matchSpanBackground       = Brushes.OrangeRed.Clone();
        static Brush replacementSpanBackground = Brushes.LightGreen.Clone();
        static Brush separatorBrush            = Brushes.Gray.Clone();

        bool UseRegularExpression;
        bool ModifyExtension;
        bool IncludeSubdirectories;
        bool RenameFolders;

        List<DirectoryNameDelta> RenamingBatch;

        class DirectoryNameDelta
        {
            public string Path;
            public List<(int offset, int length)> SpansOriginal;
            public List<(int offset, int length)> SpansReplacement;
            public string OldName;
            public string NewName;
        }
        private List<DirectoryNameDelta> GetRenamingBatch()
        {
            var batch = new List<DirectoryNameDelta>();

            var rootDirectory = textDirectory.Text;
            var pattern = textSearch.Text;
            var replacement = textReplace.Text;

            if (pattern.IsNotEmpty() && Directory.Exists(rootDirectory))
            {
                Regex regex = null;
                if (UseRegularExpression)
                {
                    try { regex = new Regex(pattern); }
                    catch { }
                }

                var directories = new List<string>() { rootDirectory };
                if (IncludeSubdirectories)
                    directories.AddRange(Directory.GetDirectories(rootDirectory, "*", SearchOption.AllDirectories));

                foreach (var directory in directories)
                {
                    var items = from item in (RenameFolders)
                                    ? Directory.GetDirectories(directory)
                                    : Directory.GetFiles(directory)
                                select (Path.GetFileName(item), (string)null);
                    if (!RenameFolders && !ModifyExtension)
                        items = from item in items
                                select (Path.GetFileNameWithoutExtension(item.Item1), Path.GetExtension(item.Item1));
                    foreach (var item in items)
                    {
                        var (name, ext) = item;

                        var matches = (regex is null)
                            ? name.Find(pattern)
                            : regex.Search(name);

                        if (matches.Count > 0)
                        {
                            var matchingRename = new DirectoryNameDelta()
                            {
                                Path = directory,
                                OldName = $"{name}{ext}",
                                SpansOriginal = matches,
                                NewName = (regex is null)
                                    ? name.Replace(pattern, replacement.FilterForFilename())
                                    : regex.Replace(name, replacement).FilterForFilename()
                            };
                            matchingRename.NewName += ext;
                            var spansReplacement = new List<(int, int)>(matches.Count);
                            int offset = 0;
                            foreach (var span in matches)
                            {
                                int spanOffset = span.offset + offset;
                                // UGLY!!! UGH SUCKS!!!
                                // Re-does the work of building the replacement string multiple times just to get the length of the span.
                                int spanLength = (regex is null)
                                    ? replacement.FilterForFilename().Length
                                    : regex.Replace(name.Substring(span.offset, span.length), replacement).FilterForFilename().Length;
                                spansReplacement.Add((spanOffset, spanLength));
                                offset += (spanLength - span.length);
                            }
                            matchingRename.SpansReplacement = spansReplacement;
                            batch.Add(matchingRename);
                        }
                    }
                }
            }

            return batch;
        }

        public MainWindow()
        {
            InitializeComponent();
            textDirectory.Text = Environment.CurrentDirectory;
            matchSpanBackground.Opacity = replacementSpanBackground.Opacity = .25;
            separatorBrush.Opacity = .5;
        }

        private void UpdatePreview(object sender, TextChangedEventArgs e)
        {
            buttonRename.IsEnabled = false;
            listPreview.Children.Clear();
            RenamingBatch = GetRenamingBatch();
            if (RenamingBatch.Count > 0)
            {
                buttonRename.IsEnabled = true;
                foreach (var renaming in RenamingBatch)
                {
                    var stack = new StackPanel() { Margin = new Thickness(8, 4, 8, 4) };

                    TextBlock makePreviewText(string text, List<(int offset, int length)> spans, Brush spanColor)
                    {
                        var textBlock = new TextBlock()
                        {
                            FontFamily = (new FontFamilyConverter()).ConvertFromString("Consolas") as FontFamily,
                            FontSize = 18,
                            TextWrapping = TextWrapping.NoWrap,
                        };

                        int lastOffset = 0;
                        foreach (var span in spans)
                        {
                            if (span.offset > lastOffset)
                                textBlock.Inlines.Add(new Run(text.Substring(lastOffset, span.offset - lastOffset)));
                            textBlock.Inlines.Add(
                                new Run(text.Substring(span.offset, span.length)) { Background = spanColor }
                            );
                            lastOffset = span.offset + span.length;
                        }
                        if (lastOffset < text.Length)
                            textBlock.Inlines.Add(new Run(text.Substring(lastOffset)));

                        return textBlock;
                    }

                    var textOriginal = makePreviewText(renaming.OldName, renaming.SpansOriginal, matchSpanBackground);
                    var textReplacement = makePreviewText(renaming.NewName, renaming.SpansReplacement, replacementSpanBackground);

                    if (renaming.Path != textDirectory.Text)
                        stack.Children.Add(new TextBlock() { Text = $"\\{renaming.Path.Replace(textDirectory.Text, null)}" });
                    stack.Children.Add(textOriginal);
                    stack.Children.Add(textReplacement);

                    Border makeSeparator() => new Border()
                    {
                        Padding = new Thickness(8, 0, 16, 0),
                        Height = 8,
                        Child = new Rectangle()
                        {
                            Height = .75,
                            Fill = separatorBrush,
                            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                        }
                    };

                    if (listPreview.Children.Count > 0)
                        listPreview.Children.Add(makeSeparator());
                    listPreview.Children.Add(stack);
                }
            }
        }

        private void RenameBatch(object sender, RoutedEventArgs e)
        {
            Assert(RenamingBatch != null);
            Assert(RenamingBatch.Count > 0);

            int successful = 0;
            var errors = new List<DirectoryNameDelta>();
            foreach (var match in RenamingBatch)
            {
                var fullOldName = Path.Combine(match.Path, match.OldName);
                var fullNewName = Path.Combine(match.Path, match.NewName);

                try
                {
                    Directory.Move(fullOldName, fullNewName);
                    successful++;
                }
                catch { errors.Add(match); }
            }

            if (successful > 0)
                MessageBox.Show($"{successful} {(RenameFolders ? "folder" : "file")}{(successful == 1 ? "" : "s")} succesfully renamed.",
                    "Success", MessageBoxButton.OK, MessageBoxImage.None);

            if (errors.Count > 0)
            {
                var rootDirectory = textDirectory.Text.TrimEnd(new[] { '\\' });
                var builder = new StringBuilder("Couldn't rename:");
                foreach (var error in errors)
                {
                    var relativeOldName = Path.Combine(error.Path, error.OldName).Replace(rootDirectory, null);
                    var relativeNewName = Path.Combine(error.Path, error.NewName).Replace(rootDirectory, null);

                    builder.AppendLine($"- \"{relativeOldName}\" to \"{relativeNewName}\"");
                }

                MessageBox.Show(builder.ToString(), "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            UpdatePreview(sender, null);
        }

        private void SelectDirectory(object sender, RoutedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog() { Description = "Select the directory containing the files to be renamed" })
            {
                dialog.ShowDialog();
                if (dialog.SelectedPath.Length > 0)
                {
                    textDirectory.Text = dialog.SelectedPath;
                    directoryErrorBorder.BorderBrush = Brushes.Transparent;
                }
            }
        }

        private void ValidatePath(object sender, TextChangedEventArgs e)
        {
            bool valid = Directory.Exists(textDirectory.Text);
            directoryErrorBorder.BorderBrush = valid ? Brushes.Transparent : Brushes.Red;
            UpdatePreview(sender, null);
        }

        private void UpdateFilters(object sender, RoutedEventArgs e)
        {
            // == true because .IsChecked is nullable
            UseRegularExpression  = checkRegex.IsChecked     == true;
            ModifyExtension       = checkExtension.IsChecked == true;
            IncludeSubdirectories = checkRecursive.IsChecked == true;
            RenameFolders         = checkFolders.IsChecked   == true;
            UpdatePreview(sender, null);
        }

        private void CheckAbout(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.F1)
                MessageBox.Show("Batch Renamer by Gorky Rojas", "About...", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    static class TextExtensions
    {
        public static List<(int offset, int length)> Find(this string text, string value)
        {
            var matches = new List<(int, int)>();
            int index = 0;
            while ((index = text.IndexOf(value, index)) != -1)
            {
                matches.Add((index, value.Length));
                index = index + value.Length;
            }
            return matches;
        }

        public static List<(int offset, int length)> Search(this Regex pattern, string text)
        {
            var regexMatches = pattern.Matches(text);
            var matches = new List<(int, int)>();
            for (int i = 0; i < regexMatches.Count; i++)
            {
                var match = regexMatches[i];
                matches.Add((match.Index, match.Length));
            }
            return matches;
        }

        public static bool IsEmpty(this string text) => text.Length == 0;
        public static bool IsNotEmpty(this string text) => text.Length > 0;

        public static string FilterForFilename(this string text) =>
            string.Join(string.Empty, text.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
    }

    [Serializable]
    class AssertionFailure : Exception
    {
        public static void Assert(bool expression)
        {
            if (!expression)
                throw new AssertionFailure();
        }

        public AssertionFailure() {}
        public AssertionFailure(string message) : base(message) { }
        public AssertionFailure(string message, Exception innerException) : base(message, innerException) { }
        protected AssertionFailure(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }
}
