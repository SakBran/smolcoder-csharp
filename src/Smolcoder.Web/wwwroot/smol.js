// Browser helpers for the smolcoder web UI.
// Blazor renders the page; this file handles what needs the browser:
// keyboard shortcuts in the composer, file uploads, Monaco, code highlighting and scrolling.

window.smol = (function () {
    const MONACO_BASE = "https://cdnjs.cloudflare.com/ajax/libs/monaco-editor/0.52.2/min/vs";
    const MYANMAR_MONO = "'JetBrains Mono', 'SF Mono', Menlo, Consolas, 'Pyidaungsu', 'Myanmar Text', 'Myanmar Sangam MN', 'Noto Sans Myanmar', monospace";

    let monacoReady = null;
    const editors = new Map();

    function initialHash() {
        const hash = window.location.hash || "";
        return hash.startsWith("#") ? hash.substring(1) : hash;
    }

    function setHash(id) {
        if (id) {
            history.replaceState(null, "", "#" + id);
        }
    }

    function load(key) {
        try {
            return localStorage.getItem(key);
        } catch (error) {
            return null;
        }
    }

    function save(key, value) {
        try {
            localStorage.setItem(key, value);
        } catch (error) {
            // Storage may be blocked. The page works without it.
        }
    }

    // Composer: Enter sends, Shift+Enter adds a line, Shift+Tab cycles the mode,
    // Escape clears the text or stops the turn. Pasted and dropped files are uploaded.
    function bindComposer(textarea, dropZone, dotnet) {
        if (!textarea || textarea.dataset.smolBound) {
            return;
        }
        textarea.dataset.smolBound = "1";

        textarea.addEventListener("keydown", function (event) {
            if (event.key === "Enter" && !event.shiftKey && !event.isComposing) {
                event.preventDefault();
                const text = textarea.value;
                textarea.value = "";
                autoGrow(textarea);
                dotnet.invokeMethodAsync("SendFromKeyboard", text);
            } else if (event.key === "Tab" && event.shiftKey) {
                event.preventDefault();
                dotnet.invokeMethodAsync("CycleMode");
            } else if (event.key === "Escape") {
                event.preventDefault();
                if (textarea.value.length > 0) {
                    textarea.value = "";
                    autoGrow(textarea);
                    dotnet.invokeMethodAsync("TextChanged", "");
                } else {
                    dotnet.invokeMethodAsync("StopTurn");
                }
            }
        });

        textarea.addEventListener("input", function () {
            autoGrow(textarea);
            dotnet.invokeMethodAsync("TextChanged", textarea.value);
        });

        textarea.addEventListener("paste", function (event) {
            const files = Array.from((event.clipboardData && event.clipboardData.files) || []);
            if (files.length > 0) {
                event.preventDefault();
                uploadAll(files, dotnet);
            }
        });

        if (dropZone) {
            dropZone.addEventListener("dragover", function (event) {
                event.preventDefault();
                dropZone.classList.add("dragging");
            });
            dropZone.addEventListener("dragleave", function () {
                dropZone.classList.remove("dragging");
            });
            dropZone.addEventListener("drop", function (event) {
                event.preventDefault();
                dropZone.classList.remove("dragging");
                const files = Array.from((event.dataTransfer && event.dataTransfer.files) || []);
                uploadAll(files, dotnet);
            });
        }
    }

    function autoGrow(textarea) {
        textarea.style.height = "auto";
        textarea.style.height = Math.min(textarea.scrollHeight, 260) + "px";
    }

    function setComposerText(textarea, text) {
        if (!textarea) {
            return;
        }
        textarea.value = text;
        autoGrow(textarea);
        textarea.focus();
    }

    function focus(element) {
        if (element) {
            element.focus();
        }
    }

    function pickFiles(input, dotnet) {
        if (!input) {
            return;
        }
        input.onchange = function () {
            const files = Array.from(input.files || []);
            input.value = "";
            uploadAll(files, dotnet);
        };
        input.click();
    }

    async function uploadAll(files, dotnet) {
        const sessionId = await dotnet.invokeMethodAsync("CurrentSessionId");
        if (!sessionId) {
            return;
        }
        for (const file of files) {
            const url = "/api/upload?sid=" + encodeURIComponent(sessionId) + "&name=" + encodeURIComponent(file.name || "pasted.png");
            try {
                const response = await fetch(url, {
                    method: "POST",
                    headers: { "content-type": file.type || "application/octet-stream" },
                    body: file,
                    credentials: "same-origin"
                });
                const result = await response.json();
                if (!response.ok) {
                    await dotnet.invokeMethodAsync("UploadFailed", result.error || ("upload failed (" + response.status + ")"));
                } else {
                    await dotnet.invokeMethodAsync("Uploaded", result.id, result.name, result.kind === "image", result.size, result.warning || null);
                }
            } catch (error) {
                await dotnet.invokeMethodAsync("UploadFailed", String(error));
            }
        }
    }

    // Keeps the transcript pinned to the bottom while new text arrives,
    // unless the reader scrolled up.
    function scrollToBottom(element, force) {
        if (!element) {
            return;
        }
        if (!element.dataset.smolScroll) {
            // Remember whether the reader is at the bottom, measured when they scroll.
            element.dataset.smolScroll = "1";
            element.dataset.stick = "1";
            element.addEventListener("scroll", function () {
                const nearBottom = element.scrollHeight - element.scrollTop - element.clientHeight < 80;
                element.dataset.stick = nearBottom ? "1" : "0";
            });
        }
        if (force || element.dataset.stick !== "0") {
            element.scrollTop = element.scrollHeight;
        }
    }

    function highlight(container) {
        if (!container || !window.hljs) {
            return;
        }
        container.querySelectorAll("pre code:not([data-smol-hl])").forEach(function (block) {
            block.setAttribute("data-smol-hl", "1");
            try {
                window.hljs.highlightElement(block);
            } catch (error) {
                // Unknown language; leave it plain.
            }
        });
    }

    function loadMonaco() {
        if (monacoReady) {
            return monacoReady;
        }
        monacoReady = new Promise(function (resolve, reject) {
            const script = document.createElement("script");
            script.src = MONACO_BASE + "/loader.min.js";
            script.onload = function () {
                window.require.config({ paths: { vs: MONACO_BASE } });
                window.MonacoEnvironment = {
                    getWorkerUrl: function () {
                        const source = "self.MonacoEnvironment = { baseUrl: '" + MONACO_BASE + "/../' };" +
                            "importScripts('" + MONACO_BASE + "/base/worker/workerMain.js');";
                        return "data:text/javascript;charset=utf-8," + encodeURIComponent(source);
                    }
                };
                window.require(["vs/editor/editor.main"], function () {
                    defineTheme();
                    resolve(window.monaco);
                });
            };
            script.onerror = reject;
            document.head.appendChild(script);
        });
        return monacoReady;
    }

    function defineTheme() {
        window.monaco.editor.defineTheme("smol-dark", {
            base: "vs-dark",
            inherit: true,
            rules: [],
            colors: {
                "editor.background": "#11141c",
                "editor.lineHighlightBackground": "#1a1f2b",
                "editorLineNumber.foreground": "#4b5468"
            }
        });
    }

    function languageFor(path) {
        const extension = (path.split(".").pop() || "").toLowerCase();
        const map = {
            js: "javascript", mjs: "javascript", cjs: "javascript", jsx: "javascript",
            ts: "typescript", tsx: "typescript", json: "json", html: "html", htm: "html",
            css: "css", scss: "scss", less: "less", md: "markdown", py: "python",
            cs: "csharp", csproj: "xml", xml: "xml", razor: "razor", cshtml: "razor",
            java: "java", go: "go", rs: "rust", rb: "ruby", php: "php", sh: "shell",
            yml: "yaml", yaml: "yaml", sql: "sql", c: "c", h: "c", cpp: "cpp", hpp: "cpp",
            kt: "kotlin", swift: "swift", dart: "dart", lua: "lua", ps1: "powershell"
        };
        return map[extension] || "plaintext";
    }

    async function showFile(element, path, content, changedLines) {
        if (!element) {
            return;
        }
        const monaco = await loadMonaco();
        let editor = editors.get(element);
        if (!editor) {
            editor = monaco.editor.create(element, {
                value: "",
                readOnly: true,
                theme: "smol-dark",
                automaticLayout: true,
                minimap: { enabled: false },
                fontFamily: MYANMAR_MONO,
                fontSize: 13,
                lineHeight: 22,
                scrollBeyondLastLine: false,
                renderWhitespace: "selection",
                unicodeHighlight: { ambiguousCharacters: false, invisibleCharacters: false, nonBasicASCII: false }
            });
            editors.set(element, editor);
        }
        const model = monaco.editor.createModel(content, languageFor(path));
        const previous = editor.getModel();
        editor.setModel(model);
        if (previous) {
            previous.dispose();
        }
    }

    function disposeEditor(element) {
        const editor = editors.get(element);
        if (editor) {
            const model = editor.getModel();
            editor.dispose();
            if (model) {
                model.dispose();
            }
            editors.delete(element);
        }
    }

    function openInNewTab(url) {
        window.open(url, "_blank", "noopener,noreferrer");
    }

    function confirmDialog(message) {
        return window.confirm(message);
    }

    function bindGlobalKeys(dotnet) {
        document.addEventListener("keydown", function (event) {
            if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "b") {
                event.preventDefault();
                dotnet.invokeMethodAsync("ToggleSidebar");
            } else if (event.ctrlKey && event.key === "`") {
                event.preventDefault();
                dotnet.invokeMethodAsync("ToggleTerminal");
            }
        });
    }

    function bindTerminalInput(input, dotnet) {
        if (!input || input.dataset.smolBound) {
            return;
        }
        input.dataset.smolBound = "1";
        const history = [];
        let position = 0;
        input.addEventListener("keydown", function (event) {
            if (event.key === "Enter") {
                event.preventDefault();
                const line = input.value;
                if (line.trim().length > 0) {
                    history.push(line);
                }
                position = history.length;
                input.value = "";
                dotnet.invokeMethodAsync("RunLine", line);
            } else if (event.key === "ArrowUp") {
                if (position > 0) {
                    position--;
                    input.value = history[position];
                    event.preventDefault();
                }
            } else if (event.key === "ArrowDown") {
                if (position < history.length) {
                    position++;
                    input.value = position < history.length ? history[position] : "";
                    event.preventDefault();
                }
            } else if (event.ctrlKey && event.key.toLowerCase() === "c" && !window.getSelection().toString()) {
                event.preventDefault();
                dotnet.invokeMethodAsync("Interrupt");
            } else if (event.ctrlKey && event.key.toLowerCase() === "l") {
                event.preventDefault();
                dotnet.invokeMethodAsync("ClearOutput");
            }
        });
    }

    return {
        initialHash: initialHash,
        setHash: setHash,
        load: load,
        save: save,
        bindComposer: bindComposer,
        setComposerText: setComposerText,
        focus: focus,
        pickFiles: pickFiles,
        scrollToBottom: scrollToBottom,
        highlight: highlight,
        showFile: showFile,
        disposeEditor: disposeEditor,
        openInNewTab: openInNewTab,
        confirmDialog: confirmDialog,
        bindGlobalKeys: bindGlobalKeys,
        bindTerminalInput: bindTerminalInput
    };
})();
