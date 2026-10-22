# Codex

Open `Window/Codex` to work with the current project through the installed Codex CLI.
The window uses the official app-server protocol and the existing ChatGPT sign-in.

## Conversations

The sidebar lists conversations for the project directory. It supports search,
pagination, rename, archive, restore, and resuming a conversation. New conversations
are created on the server when the first message is sent. New conversation and
history selection stop the current generation before switching; the original
conversation remains in Codex history. Drafts are retained while switching within
the window. Approval actions remain visible above independently scrollable details.

Messages support Markdown headings, paragraphs, lists, quotes, links and code blocks.
Execution output and file diffs can be expanded individually. Copy actions preserve
the original message or code. Scrolling up suspends automatic scrolling; the down
arrow returns to the latest output.

## Input

- Enter sends a message or adds a follow-up to the active turn.
- Shift+Enter inserts a newline.
- Ctrl+L focuses the composer; Ctrl+F opens conversation search.
- Ctrl+N opens a new conversation; Escape interrupts the active turn.
- Attach the selected project asset with the plus button, or drag an asset into the
  composer. Each attached asset can be removed before sending.
- Model and effort choices come from `model/list`. The settings view also accepts
  a model override and an executable path. By default, permissions and network
  access come from the local Codex configuration. Disable `Use local configuration`
  to explicitly override approval policy and network access for engine conversations.

Approval requests display the proposed command, working directory and reason.
User-input requests support option selection and free-text answers, including
masked secret fields. They remain pending until the user responds or the server
resolves them.

## Storage and Protocol

Editor preferences and the local active-conversation cache use YAML. Full conversation
history is owned by the installed Codex app-server using the existing `CODEX_HOME`
(or the user's `.codex` directory). The current engine project is registered with
the local Codex project API, and new durable conversations use that project ID.
The Codex icon in the conversation header opens the same thread in Codex desktop.
In an empty conversation, that icon opens the current project in Codex desktop
without sending a message. This uses the desktop's project-opening route, which
also adds the folder to its sidebar. Backend project registration alone does not
update the desktop's separate sidebar cache. An opened conversation is also
available from the desktop's recent conversations.

JSON is used only for the external JSON-RPC wire protocol. Package Skills are
supplied as developer instructions, so they do not pollute user messages or titles.

Reference: https://developers.openai.com/codex/app-server/

## Validation

`CodexCliDiscovery` covers the fake app-server workflow and composer shortcuts.
`--real-smoke` verifies the installed CLI connection and model discovery;
`--real-conversation-smoke` verifies a short response without tool use.
`--local-visibility-smoke <project-root>` creates a short conversation in an existing
project and verifies that a second local Codex connection can list and resume it.
`VulkanRendering --codex` verifies message layout, settings, streaming scroll behavior
and GPU text output at wide and compact window sizes.
