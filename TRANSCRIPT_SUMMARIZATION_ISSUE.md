# Follow-up Issue: Transcript Summarization Feature

## Issue Title
Add transcript summarization to prepend summary of older conversations when TranscriptContextMessages limit is reached

## Description
When a transcript exceeds the `TranscriptContextMessages` limit (default 10), implement a summarization feature that:

1. **Automatic Summarization**: When loading transcripts into the AI chat control, if there are more messages than the context limit, generate a summary of the older messages and prepend it as a system message.

2. **Summary Content**: The summary should capture key points, decisions, and context from the earlier conversation without exceeding token limits.

3. **UI Integration**: The summary should appear as a system message in the AI chat control but not be added to the transcript history panel (to avoid duplication).

4. **Configuration**: Add a new setting `TranscriptSummarizationEnabled` (default: true) to control whether summarization is performed.

5. **Fallback**: If summarization fails or is disabled, fall back to the current behavior of loading only the most recent N messages.

## Implementation Notes
- Use the existing AI client to generate summaries
- Store summaries in the transcript for reuse (avoid regenerating on every load)
- Consider token limits when generating summaries
- Add appropriate error handling for summarization failures

## Acceptance Criteria
- When TranscriptContextMessages=5 and transcript has 15 messages, AI chat control shows: [System summary of first 10] + last 5 messages
- History panel still shows all 15 messages
- Summarization can be disabled via configuration
- Existing behavior preserved when summarization is disabled

## Priority
Medium - Enhances user experience for long conversations but not critical for basic functionality.