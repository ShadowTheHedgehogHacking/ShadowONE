using System;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using ShadowONE.Models;
using ShadowONE.Services;

namespace ShadowONE
{
    public partial class RwVersionEditorWindow : Window
    {
        private readonly FileEntry _entry;
        private readonly Action<uint, uint, uint, uint, ushort> _onSave;

        private NumericUpDown? _versionBox;
        private NumericUpDown? _majorBox;
        private NumericUpDown? _minorBox;
        private NumericUpDown? _revisionBox;
        private TextBox? _buildNumberBox;
        private Button? _saveButton;
        private TextBlock? _errorBlock;
        private TextBlock? _hexPreview;
        private TextBlock? _fileNameBlock;

        public RwVersionEditorWindow()
        {
            InitializeComponent();
            WindowsTitleBarHelper.SetDarkTitleBar(this);
            _entry = null!;
            _onSave = (_, _, _, _, _) => { };
        }

        public RwVersionEditorWindow(FileEntry entry, Action<uint, uint, uint, uint, ushort> onSave) : this()
        {
            _entry = entry;
            _onSave = onSave;
            
            Loaded += OnLoaded;
        }

        private void InitializeComponent()
        {
            Title = "Edit RW Version";
            Width = 350;
            Height = 350;
            MinWidth = 350;
            MinHeight = 350;
            CanResize = true;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var mainPanel = new StackPanel { Margin = new Avalonia.Thickness(15), Spacing = 12 };

            _fileNameBlock = new TextBlock
            {
                Text = "File: ",
                FontWeight = Avalonia.Media.FontWeight.Bold,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            };
            mainPanel.Children.Add(_fileNameBlock);

            var fieldsPanel = new StackPanel { Spacing = 10 };

            var row1 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            row1.Children.Add(CreateFieldGroup("Version", ref _versionBox, 3, 6, 100));
            row1.Children.Add(CreateFieldGroup("Major", ref _majorBox, 0, 15, 100));
            row1.Children.Add(CreateFieldGroup("Minor", ref _minorBox, 0, 15, 100));
            fieldsPanel.Children.Add(row1);

            var row2 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            row2.Children.Add(CreateFieldGroup("Revision", ref _revisionBox, 0, 63, 100));
            row2.Children.Add(CreateBuildNumberGroup());
            
            fieldsPanel.Children.Add(row2);

            mainPanel.Children.Add(fieldsPanel);

            var endPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Spacing = 12 };
            _hexPreview = new TextBlock
            {
                Text = "Version 0.0.0.0.0000",
                FontWeight = Avalonia.Media.FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            
            endPanel.Children.Add(_hexPreview);
            
            _saveButton = new Button
            { 
                Content = "Save", 
                Width = 80, 
                Height = 28,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            _saveButton.Click += Save_Click;
            endPanel.Children.Add(_saveButton);
            
            mainPanel.Children.Add(endPanel);

            _errorBlock = new TextBlock
            {
                Foreground = Avalonia.Media.Brushes.IndianRed,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                IsVisible = false
            };
            mainPanel.Children.Add(_errorBlock);

            var hintsPanel = new StackPanel { Spacing = 2, Margin = new Avalonia.Thickness(0, 2, 0, 0) };

            var hint1 = new TextBlock
            {
                Text = "Changing this does not change the RenderWare version tags inside the actual files; only the tags present inside the .ONE archive for each file or archive.",
                Foreground = Avalonia.Media.Brushes.Gray,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            };
            hintsPanel.Children.Add(hint1);

            var hint2 = new TextBlock
            {
                Text = "The game ignores any files marked with a version higher than the game. Consider matching the game version or lower.",
                Foreground = Avalonia.Media.Brushes.Gray,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Padding = new Avalonia.Thickness(0,8,0,0)
            };
            hintsPanel.Children.Add(hint2);

            mainPanel.Children.Add(hintsPanel);

            Content = mainPanel;
        }

        private StackPanel CreateFieldGroup(string label, ref NumericUpDown? box, decimal min, decimal max, double width)
        {
            var panel = new StackPanel { Spacing = 3 };
            
            var labelBlock = new TextBlock
            {
                Text = label,
                FontSize = 12,
                FontWeight = Avalonia.Media.FontWeight.SemiBold
            };
            panel.Children.Add(labelBlock);

            box = new NumericUpDown
            {
                Minimum = min,
                Maximum = max,
                Increment = 1,
                FormatString = "0",
                Width = width,
                Height = 26,
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            panel.Children.Add(box);

            return panel;
        }

        private StackPanel CreateBuildNumberGroup()
        {
            var panel = new StackPanel { Spacing = 3 };

            panel.Children.Add(new TextBlock
            {
                Text = "Build Number (hex)",
                FontSize = 12,
                FontWeight = Avalonia.Media.FontWeight.SemiBold
            });

            _buildNumberBox = new TextBox
            {
                MaxLength = 4,
                Text = "0000",
                Width = 130,
                Height = 26,
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            _buildNumberBox.TextInput += (_, e) =>
            {
                if (e.Text != null && !e.Text.All(Uri.IsHexDigit))
                {
                    e.Handled = true;
                }
            };
            panel.Children.Add(_buildNumberBox);

            return panel;
        }

        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            if (_entry != null)
            {
                _fileNameBlock!.Text = $"File: {_entry.FileName}";
                _versionBox!.Value = _entry.RwVersion;
                _majorBox!.Value = _entry.RwMajor;
                _minorBox!.Value = _entry.RwMinor;
                _revisionBox!.Value = _entry.RwRevision;
                _buildNumberBox!.Text = _entry.RwBuildNumber.ToString("X4");
            }

            _versionBox!.ValueChanged += (_, _) => UpdatePreview();
            _majorBox!.ValueChanged += (_, _) => UpdatePreview();
            _minorBox!.ValueChanged += (_, _) => UpdatePreview();
            _revisionBox!.ValueChanged += (_, _) => UpdatePreview();
            _buildNumberBox!.TextChanged += (_, _) => UpdatePreview();

            UpdatePreview();
        }

        private bool TryReadValues(out uint version, out uint major, out uint minor, out uint revision, out ushort buildNumber)
        {
            version = major = minor = revision = 0;
            buildNumber = 0;

            // A cleared field has no value; never substitute 0 for it.
            if (_versionBox?.Value is not decimal v || _majorBox?.Value is not decimal ma ||
                _minorBox?.Value is not decimal mi || _revisionBox?.Value is not decimal re)
            {
                return false;
            }

            if (v < 3 || v > 6 || ma < 0 || ma > 15 || mi < 0 || mi > 15 || re < 0 || re > 63)
            {
                return false;
            }

            if (!ushort.TryParse(_buildNumberBox?.Text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out buildNumber))
            {
                return false;
            }

            version = (uint)v;
            major = (uint)ma;
            minor = (uint)mi;
            revision = (uint)re;
            return true;
        }

        private void UpdatePreview()
        {
            var valid = TryReadValues(out var version, out var major, out var minor, out var revision, out var buildNumber);
            _saveButton!.IsEnabled = valid;
            _errorBlock!.IsVisible = false;
            _hexPreview!.Text = valid
                ? $"Version: {version}.{major}.{minor}.{revision}.{buildNumber:X4}"
                : "Version: invalid value";
        }

        private void Save_Click(object? sender, RoutedEventArgs e)
        {
            if (!TryReadValues(out var version, out var major, out var minor, out var revision, out var buildNumber))
            {
                return;
            }

            try
            {
                _onSave(version, major, minor, revision, buildNumber);
            }
            catch (ArgumentException ex)
            {
                _errorBlock!.Text = ex.Message;
                _errorBlock.IsVisible = true;
                return;
            }

            Close();
        }
    }
}
