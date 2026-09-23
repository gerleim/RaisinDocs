# File reads and writes in RaisinDocs

*Date: 2026-09-21. Scope: every production file read and write in RaisinDocs, RaisinDocs.Editor and RaisinDocs.Viewer, looked at for share modes, atomicity, failure handling and what a failure costs. Follows the same review of StockRaisin2 the day before, and one of RaisinTerminal alongside this. **All five findings are fixed.** A defect found in the shared library is written up and fixed separately, in RaisinLibraries' `design/Durable Stores and Unreadable Files.md` — it covered RaisinDocs' `SessionStore`.*

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
| 3 | The spell-check dictionaries are saved in place and unguarded | Medium | fixed |
| 4 | The external-change reload depends on a second event arriving | Low–Medium | fixed |
| 5 | Two editors drop each other's dictionary words | Medium | fixed |

### 1. A failed save terminates the editor and takes the document with it — High, fixed

RaisinDocs.Editor has **no unhandled-exception handler at all**, and `SaveToFile` wrote with nothing around it. So Ctrl+S on a document another program had locked, a full disk or a read-only folder threw out of `Save_Click`, WPF terminated the process — and the unsaved buffer, the one thing the save existed to protect, went with it. The same held when closing: `ConfirmDiscard` calls the save, so answering Yes to "save before closing?" could crash through the close instead.

`SaveToFile` now catches the failure and changes nothing about the tab unless the write landed: it restores the path and document base it had, stays dirty, starts watching its file again, and tells you the document is still open with your changes. Because the tab stays dirty, `ConfirmDiscard` now cancels a close whose save failed.

The path and base are still assigned before the write, as they were, because a Save As may need the new base for `GetText()` to resolve the document's images — so they are put back on failure rather than set after success.

**The missing global handler was decided afterwards: log, keep what is unsaved, and exit.** Continuing was ruled out — after an exception no one planned for, the editor may hold a damaged document, and a save from there would write it over a good file. But exiting as before took every unsaved document with it, so the exit now comes after a rescue.

`App` handles the dispatcher's unhandled exceptions and the process-wide ones the same way: log the exception, write each tab with unsaved changes to `%APPDATA%\RaisinDocs\recovery` through the new `DocumentRecovery`, say how many were kept, and exit. Each document is its text plus a small `.origin` file naming the file it came from and the on-disk version it was based on; each is written on its own, so a tab whose text cannot be read — the damage may be in it — costs only itself, and nothing is written near the original. An exception on another thread cannot be survived, since the process ends when the handler returns, so the rescue is asked over to the UI thread with a five-second limit and is the whole of what is done. An unobserved task exception is only logged; it does not end the process.

At the next start the editor offers what it finds: Yes opens each with its unsaved changes, No discards them, Cancel asks again next time. A document goes back to its file if the file is still there and unchanged since the crash — into the tab the session already opened for it, or a new one — and comes back dirty, so closing it asks as it would have before the crash. If the file has changed since, it opens beside it instead, as an untitled copy named `name (recovered)` whose Save As suggests the original's folder and name, and the file's tab is left alone. Put back into the file's tab, as it first was, the rescued text replaced the newer version there — a Cancel, then work saved to the file, then a Yes at a later start — hid it, and left one Yes at the save question between the user and losing it. The offer marks each document that will open as a copy, or untitled because its file is gone, and lists them with bullets: the message box drops leading spaces, so an indented list left its first line out of step. One whose file is gone, or that never had one, comes back untitled. `DocsCanvas.RestoreUnsavedText` is what makes it dirty: it replaces the text while the document stays compared against the file's.

Nine tests pin `DocumentRecovery`: a rescued document comes back with its text, file and version; the original is never touched; untitled stays untitled; one unreadable document costs only itself and the rest keep their order — both of those fail when broken on purpose; two documents of one name are both kept; a text whose origin is lost still comes back; a discarded one is not offered again. The handlers and the offer at start live in the window and `App` and are not unit-tested: exercising them takes a real crash and a message box at the next start. **`--crash-test` is how they are checked instead.** It arms Ctrl+Alt+Shift+F12, which throws on the UI thread, and Ctrl+Alt+Shift+F11, which throws on a thread of its own — the two ways a crash reaches the handler, and the second the one that cannot be survived. The keys are dead without the switch, so no stray chord can end a real session.

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

### 3. The spell-check dictionaries are saved in place and unguarded — Medium, fixed

`SaveProjectDictionary` and `SaveUserDictionary` rewrite their files with `File.WriteAllLines`, and neither catches. The user dictionary is words collected over time; the project dictionary lives inside the project folder. Beyond the truncation window, a locked dictionary throws out of the add-word action — and with no handler, that is the same termination as finding 1, taking any unsaved documents with it.

**What was done — the crash, not the in-place write.** Both saves now catch and report through a new `SaveFailed` hook, which `SpellCheckController` wires to the canvas's logger. That meant giving the controller `ILoggingServices`, passed the way `DocsCanvas` already passes it to its other controllers; the logger is read at the moment of failure, since a host sets it after constructing the canvas. Logged as a Warning, because nothing is lost: the word joins the in-memory set before the save, so it stops being flagged at once, and every save writes the whole set, so it reaches disk at the next successful one.

Two tests, both run against the old throwing save and failing on it: adding a word to a held dictionary reports rather than throws, and a word that missed its save reaches disk at the next. The user dictionary's path is global and cannot be pointed at a test folder, but it shares the project dictionary's code exactly.

**The in-place write, fixed afterwards.** Both dictionaries are written through `SafeFile.WriteWithStream`, so one is replaced whole or not at all rather than truncated at the start of every save — the user dictionary being words gathered over months, and a kill inside an in-place rewrite able to empty it. Before switching, the new write was checked against the old for the exact bytes it produces, because project dictionaries sit in project folders under version control and a changed line ending or a byte-order mark would show every one of them as modified: they are identical, UTF-8 without a mark and CRLF after every line.

Two more tests. A reader holding the dictionary across a save still reads the version it opened, whole, with no scratch file left — which fails against the in-place write. And the file is written exactly as before, byte for byte — which passes against both writes, and is meant to: it is the guard that the switch changed nothing it should not have.

The swap carries the cost measured for finding 2: `File.Replace` needs delete access, so a program holding a dictionary open without sharing delete now blocks its save. Here that is the mildest failure in this document — reported as a Warning, the word already in memory, written at the next save that lands.

### 4. The external-change reload depends on a second event arriving — Low–Medium, fixed

`ReloadFromDisk` reads with `File.ReadAllText` when the file watcher reports a change, and on failure writes to `Trace`, which nothing is listening to. Probed rather than assumed, with a writer that holds the file across two chunks:

| watcher event | what the reload saw |
|---|---|
| first, while the writer still held the file | **denied** |
| second, after the writer closed | the complete new content |

So it recovers — but only because a second event arrived after the writer closed. A writer that finishes inside the first event, or a burst of events the watcher coalesces, leaves nothing to recover with, and the tab keeps stale text that a later save would write over the external change. The first failure is invisible either way.

**Widening the read would make this worse, not better.** On that first event a shared read succeeds — and loads the half-written file into the editor as though it were the document. The restrictive read is what keeps partial content out. The fix is to wait for the file to settle and retry, which is what RaisinTerminal's automation channel does by polling instead of watching. It is the reverse of the StockRaisin2 review, where shared reads were the answer.

**Looking closer, the tab with unsaved edits was worse off than the reload.** With `PromptOnExternalChanges` off, an external change reloaded over the edits and discarded them without a word. With it on, the one question offered losing your edits or keeping them — and keeping them was remembered nowhere, so the next Ctrl+S replaced the other program's work unasked. The question came on the first event, often mid-write, and could come again for each event of the same burst. And a tab reused from an empty Untitled one on open never watched its file at all.

**What was done**, as four agreed points:

1. **Unsaved edits are never discarded unasked.** A clean tab takes the new version silently, as before; a tab with unsaved edits always asks, whatever `PromptOnExternalChanges` says. That left the setting with nothing to decide — clean tabs always reloaded, and nothing in either app's UI set it — so it was removed from `DocsEditorState`. Saved editor state that still names it loads as before, since the reader skips names it does not know.
2. **One question, once the file has settled.** The watcher's callback reads through `SettledFile.Read` off the UI thread before anything is shown: the default share mode is refused while a writer holds the file, so it retries, and a read that succeeds is kept only if the file is unchanged a moment later, which catches a writer that opens it more than once. Up to fifteen tries of 200 ms. A change arriving while the question is open is not asked about again, and a Yes loads whatever is on disk by then.
3. **Nothing is replaced without a question.** Each tab keeps a `DiskStamp` — last write time and length — of the version it was loaded from, last saved or last reloaded. A save to that same file checks it first and, if another program has written a version no one was asked about, asks before replacing it; No cancels the save and keeps the tab dirty, so a close is cancelled too. That covers what the watcher misses: a reload that never settled, or a change that went unseen, leaves the stamp behind, and the save asks. Answering No to the reload question is the decision and is not asked again, as in Notepad++: it records the version it was about as seen, so the save goes through. At first it recorded nothing and the save asked the same thing a second time; VS Code, which asks only at save, asks once too. A change that lands while the question is open is still asked about at save. Save As to another file is left to its dialog's own overwrite question.
4. **The questions say what each answer costs.** The reload question now reads: Yes, load their version and your unsaved changes are lost; No, keep yours and saving it will replace theirs. A Yes whose file cannot be read by then says so and leaves your version open.

The reused Untitled tab now records its stamp and watches its file like any other. The previous `Trace` on a failed reload is gone: a failure leaves the stamp unchanged, which the save then asks about.

**A file deleted by another program now asks, as Notepad++ does.** It said nothing before: the watcher ignored deletions, and the tab looked saved while its text existed nowhere else. `FileChangeWatcher` now reports a deletion — once, from its own event or the poll, and the file coming back as a change — and the editor waits a second before asking, so a program that saves by deleting and rewriting does not set it off. Yes keeps the tab, titled `name (deleted)` and counted as unsaved, so closing asks and Ctrl+S writes the file back; No closes it, and says so when that loses unsaved edits. An atomic replace sends a deletion before its rename, so any report but a deletion marks the file present again — without that, the poll reported the replaced file coming back as a second change, which two existing watcher tests caught. The Viewer ignores deletions and keeps showing what it had. Two watcher tests: a deletion is reported once, and a deletion then a return is reported as both — each fails without the once-only guard.

### 5. Two editors drop each other's dictionary words — Medium, fixed

Found while fixing finding 3, and not about how the file is written — making the write atomic does nothing for it. The spell checker is not one per process but one per editor: each `DocsCanvas` builds its own `SpellCheckController`, which builds its own `SpellCheckService`, which reads the dictionaries once when it starts and never again. Every add then rewrites the whole file from that editor's private copy. Proven with two services over one dictionary, the way two tabs hold it:

| step | file afterwards |
|---|---|
| both editors load `Existing` | `Existing` |
| the first adds `Alphaword` | `Alphaword`, `Existing` |
| the second adds `Betaword` | `Betaword`, `Existing` |

`Alphaword` is gone from disk with nothing said, though the first editor goes on treating it as spelled correctly until it is closed. Two tabs in one RaisinDocs window are enough. The user dictionary is one file for everything, so RaisinDocs and RaisinTerminal2 — which embeds this editor in several places — do it to each other as well, wherever spell-check is on in both.

**What was done:** a save merges rather than overwrites. Just before writing, it reads the file again and joins the words on disk to the ones in memory, so a save only ever adds — and the editor saving learns the other's words as it does, rather than flagging them until it is reopened. Sharing one service per dictionary was the other way, and was not taken: it reaches into how the canvas is built, and would still leave RaisinDocs and RaisinTerminal2 as two processes each with its own.

If that read fails, the save is skipped and reported through `SaveFailed`, the same Warning as finding 3. Saving without it would be the very overwrite this fixes; the word stays in memory and the next save that can read the file carries it.

Two things it does not do. Two saves in the same few milliseconds, from two editors, can still race between one's read and the other's write — adding a word is a hand action, so that is left. And a word removed from the file by hand while an editor is open comes back at its next save, as it always did, since the editor still holds it; merging only adds.

Three tests, each failing when the merge is taken out: two editors keep each other's words, the editor that saves learns the words it merged, and a dictionary that can be replaced but not read back is left alone, its words still there once a later save can read them.

## Tests

RaisinDocs passes all three test projects, with its two long-standing skips unchanged; the seven dictionary tests above are this review's, `DocumentRecoveryTests` adds nine for the crash handler under finding 1, and `SettledFileTests` adds six for finding 4: a file still being written is read once the writer finishes, a file that changes just after a read is read again — both fail against a single read — one that never settles gives nothing rather than part of it, one that is gone gives nothing, and the stamp both names the version read and changes with another program's write. The fixes for findings 1 and 4 that live in `MainWindow`'s code-behind — the save guard, the stamp check before a save, and the questions — are not unit-tested: exercising them means standing up a WPF window and answering its message boxes, a test of more weight than it would protect. What can be tested without a window — the settled read and the stamp — is.

**Rechecked 2026-09-23**, after the reviews were revised: 1,035 unit tests, 654 conformance and 999 UI pass, none failing, and the two skips are still the UI project's. The solution builds with no errors and no warnings — the one failure on the way there was the Viewer's copy of `RaisinDocs.dll`, locked by a running Viewer, which is stale binaries rather than a broken build.
