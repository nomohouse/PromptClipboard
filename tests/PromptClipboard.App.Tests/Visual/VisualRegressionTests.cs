namespace PromptClipboard.App.Tests.Visual;

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PromptClipboard.App.ViewModels;
using PromptClipboard.Domain.Entities;
using VerifyXunit;

[Trait("Category", "Visual")]
[Collection("WpfVisual")]
public sealed class VisualRegressionTests
{
    [Fact]
    public async Task PaletteCard_DefaultState_MatchesBaseline()
    {
        await StaHelper.RunOnStaThreadAsync(async () =>
        {
            var vm = CreateViewModel("Test Prompt", "Short body text", isPinned: false, tags: []);
            var card = BuildProductionCard(vm);
            var png = WpfRenderHelper.RenderToPng(card, 400, 80);
            await Verifier.Verify(png, "png");
        });
    }

    [Fact]
    public async Task PaletteCard_ExpandedState_MatchesBaseline()
    {
        await StaHelper.RunOnStaThreadAsync(async () =>
        {
            var vm = CreateViewModel("Long Prompt", "Line1\nLine2\nLine3\nLine4\nLine5\nLine6", isPinned: false, tags: []);
            var card = BuildProductionCard(vm);
            var png = WpfRenderHelper.RenderToPng(card, 400, 160);
            await Verifier.Verify(png, "png");
        });
    }

    [Fact]
    public async Task PaletteCard_PinnedWithTags_MatchesBaseline()
    {
        await StaHelper.RunOnStaThreadAsync(async () =>
        {
            var vm = CreateViewModel("Pinned Card", "Body content", isPinned: true, tags: ["tag1", "tag2"]);
            var card = BuildProductionCard(vm);
            var png = WpfRenderHelper.RenderToPng(card, 400, 100);
            await Verifier.Verify(png, "png");
        });
    }

    private static PromptItemViewModel CreateViewModel(string title, string body, bool isPinned, string[] tags)
    {
        var prompt = new Prompt
        {
            Id = 1,
            Title = title,
            Body = body,
            IsPinned = isPinned
        };
        prompt.SetTags(tags);
        return new PromptItemViewModel(prompt);
    }

    /// <summary>
    /// Builds a card matching the production DataTemplate from PaletteWindow.xaml (lines 191-312).
    /// Uses production PromptItemViewModel, Converters, and color scheme.
    ///
    /// Limitation: the layout is recreated in C#, not loaded from the actual XAML DataTemplate.
    /// XAML-only changes (margins, triggers, new elements) won't be caught until the template
    /// is extracted to a shared ResourceDictionary that both PaletteWindow and tests can reference.
    /// </summary>
    private static Border BuildProductionCard(PromptItemViewModel vm)
    {
        // Production color scheme from PaletteWindow.xaml Window.Resources
        var cardBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x24, 0x25, 0x40)));
        var textBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xe2, 0xe8, 0xf0)));
        var textDimBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x94, 0xa3, 0xb8)));
        var accentBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x63, 0x66, 0xf1)));
        var tagBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x1e, 0x1f, 0x3a)));
        var badgeBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x63, 0x66, 0xf1)));

        var grid = new Grid();
        for (int i = 0; i < 5; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Row 0: Title + Pin icon (matching production DockPanel)
        var titlePanel = new DockPanel();
        Grid.SetRow(titlePanel, 0);

        var actionButtons = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(actionButtons, Dock.Right);

        // Pin button — uses production BoolToPinColorConverter
        var pinForeground = (Brush)Converters.BoolToPinColorConverter.Convert(
            vm.Prompt.IsPinned, typeof(Brush), null!, System.Globalization.CultureInfo.InvariantCulture);
        actionButtons.Children.Add(new TextBlock
        {
            Text = "\uE734",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 14,
            Foreground = pinForeground,
            Padding = new Thickness(4),
            Margin = new Thickness(2, 0, 2, 0),
            VerticalAlignment = VerticalAlignment.Center
        });

        // Edit, Copy, Delete icons
        foreach (var glyph in new[] { "\uE70F", "\uE8C8", "\uE74D" })
        {
            actionButtons.Children.Add(new TextBlock
            {
                Text = glyph,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 14,
                Foreground = textDimBrush,
                Padding = new Thickness(4),
                Margin = new Thickness(2, 0, 2, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        titlePanel.Children.Add(actionButtons);

        var titleStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        titleStack.Children.Add(new TextBlock
        {
            Text = vm.Prompt.Title,
            Foreground = textBrush,
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            Margin = new Thickness(0, 0, 8, 0)
        });

        // Template badge — production uses HasTemplateConverter
        var hasTemplate = (bool)Converters.HasTemplateConverter.Convert(
            vm.Prompt.Body, typeof(bool), null!, System.Globalization.CultureInfo.InvariantCulture);
        if (hasTemplate)
        {
            titleStack.Children.Add(new Border
            {
                Background = badgeBrush,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Child = new TextBlock { Text = "Template", Foreground = Brushes.White, FontSize = 10 }
            });
        }

        titlePanel.Children.Add(titleStack);
        grid.Children.Add(titlePanel);

        // Row 1: Body preview — uses production PreviewText from PromptItemViewModel
        var bodyPreview = new TextBlock
        {
            Text = vm.PreviewText,
            Foreground = textDimBrush,
            FontSize = 12,
            TextTrimming = vm.IsExpanded ? TextTrimming.None : TextTrimming.CharacterEllipsis,
            MaxHeight = vm.IsExpanded ? 180 : 36,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(bodyPreview, 1);
        grid.Children.Add(bodyPreview);

        // Row 2: Meta label (visible when long body and collapsed)
        if (vm.IsLongBody && !vm.IsExpanded)
        {
            var metaLabel = new TextBlock
            {
                Text = vm.MetaLabel,
                Foreground = textDimBrush,
                FontSize = 11,
                FontStyle = FontStyles.Italic,
                Opacity = 0.7,
                Margin = new Thickness(0, 2, 0, 0)
            };
            Grid.SetRow(metaLabel, 2);
            grid.Children.Add(metaLabel);
        }

        // Row 3: Toggle button (visible when long body)
        if (vm.IsLongBody)
        {
            var toggleLabel = new TextBlock
            {
                Text = vm.ToggleLabel,
                Foreground = accentBrush,
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            Grid.SetRow(toggleLabel, 3);
            grid.Children.Add(toggleLabel);
        }

        // Row 4: Tags — uses production JsonToTagListConverter and TagsNotEmptyConverter
        var tagsVisible = (Visibility)Converters.TagsNotEmptyConverter.Convert(
            vm.Prompt.TagsJson, typeof(Visibility), null!, System.Globalization.CultureInfo.InvariantCulture);
        if (tagsVisible == Visibility.Visible)
        {
            var tagsList = (List<string>)Converters.JsonToTagListConverter.Convert(
                vm.Prompt.TagsJson, typeof(List<string>), null!, System.Globalization.CultureInfo.InvariantCulture);

            var tagsPanel = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            foreach (var tag in tagsList)
            {
                tagsPanel.Children.Add(new Border
                {
                    Background = tagBrush,
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 2, 6, 2),
                    Margin = new Thickness(0, 0, 4, 2),
                    Child = new TextBlock
                    {
                        Foreground = textDimBrush,
                        FontSize = 11,
                        Text = $"#{tag}"
                    }
                });
            }
            Grid.SetRow(tagsPanel, 4);
            grid.Children.Add(tagsPanel);
        }

        return new Border
        {
            Background = cardBrush,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Child = grid
        };
    }

    private static SolidColorBrush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}
