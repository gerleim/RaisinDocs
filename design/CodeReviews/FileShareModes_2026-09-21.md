# File reads and writes in RaisinDocs

*Date: 2026-09-21. Scope: every production file read and write in RaisinDocs, RaisinDocs.Editor and RaisinDocs.Viewer, looked at for share modes, atomicity, failure handling and what a failure costs. Follows the same review of StockRaisin2 the day before, and one of RaisinTerminal alongside this. **Findings 1 and 2 are fixed; 3 is half fixed — its crash, not its in-place write; 4 is open.** A defect found in the shared library is written up and fixed separately, in RaisinLibraries' `design/Durable Stores and Unreadable Files.md` — it covered RaisinDocs' `SessionStore`.*

## What RaisinDocs already gets right

- **The save marks the document clean only after the write.** `SaveToFile` calls `MarkClean()` after the write succeeds, which is the order RaisinTerminal had backwards in three places. So a failed save here never left a document believing it was saved.
- **Closing is guarded by the dirty flag.** `ConfirmDiscard` saves and then returns `!IsDirty`, so once a failed save leaves the document dirty, the close is cancelled rather than proceeding. That is what made finding 1 a one-place fix.
- **The external-change reload refuses partial content**, and that is correct rather than an accident — see finding 4, where widening it would be the wrong fix.
- **The app log is guarded**, and the dictionary files are only created when absent.

## Findings

| # | finding | severity | state |
|---|---|---|---|
| 1 | A failed save terminates the editor and takes the unsaved document with it | High | fixed |
| 2 | Documents are saved in place | Medium–High | fixed |
| 3 | The spell-check dictionaries are saved in place and unguarded | Medium | crash fixed; in place open |
| 4 | The external-change reload depends on a second event arriving | Low–Medium | open |

### 1. A failed save terminates the editor and takes the document with it — High, fixed

RaisinDocs.Editor has **no unhandled-exception handler at all**, and `SaveToFile` wrote with nothing around it. So Ctrl+S on a document another program had locked, a full disk or a read-only folder threw out of `Save_Click`, WPF terminated the process — and the unsaved buffer, the one thing the save existed to protect, went with it. The same held when closing: `ConfirmDiscard` calls the save, so answering Yes to "save before closing?" could crash through the close instead.

`SaveToFile` now catches the failure and changes nothing about the tab unless the write landed: it restores the path and document base it had, stays dirty, starts watching its file again, and tells you the document is still open with your changes. Because the tab stays dirty, `ConfirmDiscard` now cancels a close whose save failed.

The path and base are still assigned before the write, as they were, because a Save As may need the new base for `GetText()` to resolve the document's images — so they are put back on failure rather than set after success.

**The missing global handler is left as it was.** Any other unexpected exception still terminates the editor. What a handler ought to do — log and continue, or log and exit — is a decision about the whole application rather than about saving, so it is recorded here and not made.

### 2. Documents are saved in place — Medium–High, fixed

`SaveToFile` writes with `File.WriteAllText`, which truncates and then refills. A kill, crash or power loss inside that window leaves the document truncated or empty — and for a document editor that is the core job, and often a file inside a repository, where git or another editor may also be reading it. `SafeFile.WriteAllText` would make the swap atomic and is the natural fix; RaisinDocs already references Raisin.Core.

**What was done:** `SaveToFile` writes through `SafeFile.WriteAllText`, fully qualified rather than importing `Raisin.Core` into the window. The file watcher needed nothing: `FileChangeWatcher` already reports a rename onto its file as a modification — its own comment calls that shape an atomic save — and the watcher is suppressed across our own save and re-armed afterwards in any case.

The swap has one cost worth knowing, and it was measured rather than assumed. `File.Replace` needs delete access to the file it replaces, so a program holding the document open while sharing read and write but *not* delete now blocks the save, where the in-place write would have gone through underneath it:

| a reader holds the file, sharing | in-place | atomic |
|---|---|---|
| `Read` | blocked | blocked |
| `ReadWrite` | saved | **blocked** |
| `ReadWrite \| Delete` | saved | saved |

Such holders are rare for a document, and the failure is now the one finding 1 made safe: the document stays open and dirty and you are told. The fix is not unit-tested, living in the window's code-behind; the same swap is pinned by tests in RaisinTerminal, where both fail against an in-place write.

### 3. The spell-check dictionaries are saved in place and unguarded — Medium, crash fixed

`SaveProjectDictionary` and `SaveUserDictionary` rewrite their files with `File.WriteAllLines`, and neither catches. The user dictionary is words collected over time; the project dictionary lives inside the project folder. Beyond the truncation window, a locked dictionary throws out of the add-word action — and with no handler, that is the same termination as finding 1, taking any unsaved documents with it.

It reaches further than RaisinDocs. RaisinTerminal2 embeds this editor for task documents and attachments and leaves unexpected exceptions unhandled on purpose, so wherever spell-check is switched on in its editors — `SpellCheckEnabled` travels in the editor state it applies — a held dictionary ended the terminal and every session in it.

**What was done — the crash, not the in-place write.** Both saves now catch and report through a new `SaveFailed` hook, which `SpellCheckController` wires to the canvas's logger. That meant giving the controller `ILoggingServices`, passed the way `DocsCanvas` already passes it to its other controllers; the logger is read at the moment of failure, since a host sets it after constructing the canvas. Logged as a Warning, because nothing is lost: the word joins the in-memory set before the save, so it stops being flagged at once, and every save writes the whole set, so it reaches disk at the next successful one.

Two tests, both run against the old throwing save and failing on it: adding a word to a held dictionary reports rather than throws, and a word that missed its save reaches disk at the next. The user dictionary's path is global and cannot be pointed at a test folder, but it shares the project dictionary's code exactly.

The dictionaries are still written in place with `File.WriteAllLines`, so a kill inside a save can still truncate one. That half was not asked for in this change and waits; `SafeFile.WriteWithStream` would make it atomic in a line.

### 4. The external-change reload depends on a second event arriving — Low–Medium, open

`ReloadFromDisk` reads with `File.ReadAllText` when the file watcher reports a change, and on failure writes to `Trace`, which nothing is listening to. Probed rather than assumed, with a writer that holds the file across two chunks:

| watcher event | what the reload saw |
|---|---|
| first, while the writer still held the file | **denied** |
| second, after the writer closed | the complete new content |

So it recovers — but only because a second event arrived after the writer closed. A writer that finishes inside the first event, or a burst of events the watcher coalesces, leaves nothing to recover with, and the tab keeps stale text that a later save would write over the external change. The first failure is invisible either way.

**Widening the read would make this worse, not better.** On that first event a shared read succeeds — and loads the half-written file into the editor as though it were the document. The restrictive read is what keeps partial content out. The fix is to wait for the file to settle and retry, which is what RaisinTerminal's automation channel does by polling instead of watching. It is the reverse of the StockRaisin2 review, where shared reads were the answer.

## Tests

RaisinDocs passes all three test projects, with its two long-standing skips unchanged; the two dictionary tests above are this review's. The fix for finding 1 lives in `MainWindow`'s code-behind and is not unit-tested: exercising it means standing up a WPF window and a locked file, and a test of that weight to pin a `catch` would cost more than it protects.
