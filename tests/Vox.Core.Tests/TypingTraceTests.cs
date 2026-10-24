using Vox.Core.Text;

namespace Vox.Core.Tests;

/// <summary>Security audit L5 (25.10): self-learning takes a fix typed by hand, not a page's swap after any key.</summary>
public class TypingTraceTests
{
    private const string Inserted = "Настрой кубернетис сегодня.";
    private const string Before = "Задача: " + Inserted;
    private const string After = "Задача: Настрой Kubernetes сегодня.";

    // The field after each of the user's editing keys, then whether the final fix counts as typed.
    private static bool Typed(string after, params string[] samples)
    {
        var trace = new TypingTrace(Before, Inserted);
        foreach (var s in samples)
        {
            trace.Add(s);
        }

        var (_, correct) = CorrectionDetector.Detect(Before, after, Inserted) ?? throw new InvalidOperationException("not a fix");
        return trace.ShowsTyping(after, correct);
    }

    [Fact]
    public void A_word_retyped_letter_by_letter_is_typed() =>
        Assert.True(Typed(After,
            "Задача: Настрой кубернети сегодня.", "Задача: Настрой кубер сегодня.", "Задача: Настрой  сегодня.",
            "Задача: Настрой Kube сегодня.", After));

    [Fact]
    public void A_word_selected_and_typed_over_is_typed() =>
        Assert.True(Typed(After, "Задача: Настрой K сегодня.", "Задача: Настрой Kuberne сегодня."));

    [Fact]
    public void A_fix_with_the_text_going_on_is_typed()
    {
        const string after = "Задача: Настрой Kubernetes сегодня. Потом";
        Assert.True(Typed(after, "Задача: Настрой Kub сегодня.", "Задача: Настрой Kubernetes сегодня.", "Задача: Настрой Kubernetes сегодня. По"));
    }

    // The audit's scenario: the user goes on typing after the dictation, a page script swaps the word in one step.
    [Fact]
    public void A_swap_in_one_step_while_the_user_types_elsewhere_is_not()
    {
        const string after = "Задача: Настрой Kubernetes сегодня. Дальше";
        Assert.False(Typed(after,
            "Задача: Настрой кубернетис сегодня. Д", "Задача: Настрой кубернетис сегодня. Да",
            "Задача: Настрой Kubernetes сегодня. Дал", after));
    }

    [Fact]
    public void A_swap_after_a_key_that_changed_nothing_is_not() => Assert.False(Typed(After, Before, After));

    [Fact]
    public void No_editing_key_at_all_is_not() => Assert.False(Typed(After));

    // Typing elsewhere in the dictation does not vouch for a word the user never touched.
    [Fact]
    public void An_edit_of_another_word_does_not_vouch_for_the_fix() =>
        Assert.False(Typed(After, "Задача: Настр кубернетис сегодня.", "Задача: Настрой кубернетис сегодня.", After));

    // A whole word pasted over the selection is one step too: not automatic, Ctrl+S remembers it (FieldWatcher).
    [Fact]
    public void A_pasted_word_is_not_typed() => Assert.False(Typed(After, After));
}
