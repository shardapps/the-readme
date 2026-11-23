# Changelog

## 1.1.0-alpha.5

- Use the Windows-provided desktop runtime. The unpacked Store payload is under 2 MB per architecture. The final alpha Store bundle requires Windows 11 22H2 or later. The x64 setup EXE also works on Windows 10 22H2.
- Reduce working memory with software rendering, bounded image decoding, a smaller editor undo budget for large files, and lighter transitions on lower-spec PCs and large displays.
- Load long Markdown previews one page at a time. Find, internal links, and the heading outline navigate across pages, and the full document stays available in Edit.
- Measure private working set during normal and maximized editing on x64 and Arm64. Enforce a 50 MB budget for the tested workflow and for the app payload.

## 1.1.0-alpha.4

- Use a themed in-app prompt for unsaved documents. Choose Save, Discard changes, or Keep editing when closing or switching files.

## 1.1.0-alpha.3

- Choose Read, Edit, Split, or Last used when opening Markdown. The sidebar starts closed and remembers when you open it.
- Click rendered text and type to start editing at that position. Turn the behavior off in Settings if you prefer.
- Find and replace individual or all matches with optional case sensitivity and undo. Use Ctrl+1, Ctrl+2, and Ctrl+3 to switch views.
- Refined all seven color themes and added subtle transitions for documents, the sidebar, Settings, and Find.

## 1.1.0-alpha.2

- Edit and save HDL, Python, C, C++, Java, and other supported source or text files. Save As starts with the original file type.
- Try the built-in HDL example and choose whether the editor keeps indentation on new source lines.
- Underline text in Markdown using the `++text++` extension, with a matching live preview.
- Use subtler view transitions or turn them off in Settings.

## 1.1.0-alpha.1

- Added Markdown and plain-text editing, portable Markdown formatting, live split preview, undo and redo, and Save As.
- Added read-only source viewing for Python, C, C++, Java, HDL, and many other text-based formats with wrapping and go-to-line.
- Added Ocean, Dusk, and Graphite. Settings closes when clicking outside it, pressing Escape, or switching windows.
- Removed fixed reading-width limits, added 60–220% zoom and Ctrl+mouse-wheel zoom, and made split editing adapt to narrow and large windows.
- Expanded the editable sample, added a Python example, and added smoke checks for format detection, saving, draft protection, and responsive layout.

## 1.0.0

- Added a native Windows Markdown, README, and plain-text reader with drag and drop, a heading outline, search, and source view.
- Added four reading palettes, system theme following, zoom, and keyboard shortcuts.
- Added x64 and Arm64 EXE installers with Default Apps registration and optional user PATH.
- Added a Windows build and smoke-test gate, dependency checks, and public project policies.
