namespace PromptClipboard.App.Tests;

using PromptClipboard.App.ViewModels;
using PromptClipboard.Application.Services;

public sealed class TemplateDialogViewModelTests
{
    private readonly TemplateDialogViewModel _sut = new();
    private bool? _closeResult;

    public TemplateDialogViewModelTests()
    {
        _sut.RequestClose += result => _closeResult = result;
    }

    [Fact]
    public void LoadVariables_PopulatesFields()
    {
        var vars = new[]
        {
            new TemplateEngine.TemplateVariable("name", null),
            new TemplateEngine.TemplateVariable("greeting", "hello")
        };

        _sut.LoadVariables(vars);

        Assert.Equal(2, _sut.Fields.Count);
        Assert.Equal("name", _sut.Fields[0].Name);
        Assert.Equal(string.Empty, _sut.Fields[0].Value);
        Assert.Equal("greeting", _sut.Fields[1].Name);
        Assert.Equal("hello", _sut.Fields[1].Value);
    }

    [Fact]
    public void GetValues_ReturnsDictionary()
    {
        var vars = new[]
        {
            new TemplateEngine.TemplateVariable("key1", null),
            new TemplateEngine.TemplateVariable("key2", "default2")
        };
        _sut.LoadVariables(vars);
        _sut.Fields[0].Value = "value1";

        var result = _sut.GetValues();

        Assert.Equal("value1", result["key1"]);
        Assert.Equal("default2", result["key2"]);
    }

    [Fact]
    public void Confirm_RaisesRequestCloseWithTrue()
    {
        _sut.ConfirmCommand.Execute(null);

        Assert.True(_closeResult);
    }

    [Fact]
    public void Cancel_RaisesRequestCloseWithFalse()
    {
        _sut.CancelCommand.Execute(null);

        Assert.False(_closeResult);
    }

    [Fact]
    public void LoadVariables_ClearsExistingFields()
    {
        var firstVars = new[] { new TemplateEngine.TemplateVariable("a", null) };
        _sut.LoadVariables(firstVars);
        Assert.Single(_sut.Fields);

        var secondVars = new[]
        {
            new TemplateEngine.TemplateVariable("x", null),
            new TemplateEngine.TemplateVariable("y", null)
        };
        _sut.LoadVariables(secondVars);

        Assert.Equal(2, _sut.Fields.Count);
        Assert.Equal("x", _sut.Fields[0].Name);
        Assert.Equal("y", _sut.Fields[1].Name);
    }
}
