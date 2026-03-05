namespace PromptClipboard.App.Handlers;

internal static class SeedCatalog
{
    public static readonly IReadOnlyList<(string SeedKey, string Title, string Body, string Tags, string Lang)> V1StableItems =
    [
        ("v1_email_professional_reply",
            "Email: Professional reply",
            "Write a professional email reply about \"{{topic}}\". Tone: {{tone|default=polite and professional}}. Target audience: {{audience|default=colleagues}}.",
            "[\"email\",\"work\"]",
            ""),
        ("v1_jira_task_description",
            "Jira: Task description",
            "**Task:** {{task_name}} **Description:** {{description}} **Acceptance criteria:** - {{criteria_1}} - {{criteria_2}} - {{criteria_3}} **Technical details:** {{tech_details}}",
            "[\"jira\",\"work\",\"tasks\"]",
            ""),
        ("v1_code_review_comment",
            "Code: Review comment",
            "Improvement suggestion: {{suggestion}} Reason: {{reason}} Example: ```{{example}}```",
            "[\"code\",\"review\"]",
            ""),
        ("v1_code_analysis",
            "Code analysis",
            "Analyze the following code and provide: 1. Potential issues 2. Optimization opportunities 3. Readability improvements Code: {{code}}",
            "[\"code\",\"analysis\"]",
            ""),
        ("v1_text_translation",
            "Text translation",
            "Translate the following text to {{target_lang|default=english}}, preserving the original style and tone:\n\n{{text}}",
            "[\"translation\",\"text\"]",
            ""),
    ];
}
