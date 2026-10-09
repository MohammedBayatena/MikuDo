namespace MikuDo.Models;

/// <summary>
/// The short tips the sidebar shows, one at a time. Each names a feature by
/// what the user does, so it can be tried straight away. A command's keys are
/// written as its id in braces, {quick.open}, and filled in as the user has
/// them set; a tip for a command left without keys is passed over.
/// </summary>
public static class Tips
{
    public static readonly string[] All =
    {
        "Press {quick.open} to search tasks, notes, settings and commands at once.",
        "Type > in the search bar to run any command by its name.",
        "Any command's keys can be changed in Settings, under Keyboard shortcuts.",
        "{board.newtask} starts a new task from anywhere.",
        "Ctrl-click cards to pick several, then set their priority and labels in one go.",
        "Drag a card onto the strip that opens beside another card's grip to make it that task's subtask.",
        "Turn on Group by parent to fold subtasks under the task they belong to.",
        "In All Tasks, each list has its own search and sort in its header.",
        "Dictate on a task's page, and pick Exact or Write up for just that task.",
        "Select part of a description and press Rewrite to have the AI polish only that.",
        "{find.open} finds words in the note or task on screen, by whole word or regex too; {find.next} goes to the next.",
        "Select checklist items in a task's description and make them its subtasks in one go.",
        "The Comments tab keeps notes and decisions apart from the description.",
        "In AI Import, [x] marks a task done and [ ] marks it to do.",
        "{capture.screenshot} captures your screen into a new task.",
        "Ctrl+V on a task's page attaches a copied image or screenshot.",
        "Click an image to see it full screen; the wheel zooms and Ctrl+S saves it.",
        "{theme.toggle} switches between light and dark.",
        "{window.sidebar} hides the sidebar when you want more room.",
        "Drag the edge of a task's side panel to widen it; double-click to reset.",
        "Double-click a workspace to rename it.",
        "The dashboard can show one workspace or all of them: pick at its top right.",
        "Undo on the board toolbar takes back moves, priorities, links and more.",
        "Export turns the lists on screen into Markdown, long notes summarized."
    };

    /// <summary>The tip with each command's keys written in; null when one of them has none.</summary>
    public static string? Resolve(string tip)
    {
        var text = tip;
        for (var open = text.IndexOf('{'); open >= 0; open = text.IndexOf('{'))
        {
            var close = text.IndexOf('}', open);
            if (close < 0) break;
            var keys = Services.Shortcuts.GestureText(text[(open + 1)..close]);
            if (keys == null) return null;
            text = text[..open] + keys + text[(close + 1)..];
        }
        return text;
    }
}
