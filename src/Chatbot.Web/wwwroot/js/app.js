(() => {
    "use strict";

    const root = document.getElementById("app");
    const API = root.dataset.apiBase.replace(/\/+$/, "");
    const AUTH_KEY = "chatbot.auth";
    const MAX_MESSAGE_LENGTH = 8000;

    const $ = (id) => document.getElementById(id);
    const el = {
        authView: $("auth-view"), authForm: $("auth-form"), authError: $("auth-error"), authSubmit: $("auth-submit"),
        chatView: $("chat-view"), sidebar: $("sidebar"), backdrop: $("sidebar-backdrop"),
        list: $("conversation-list"), listEmpty: $("conversation-empty"), newChat: $("new-chat"),
        userName: $("user-name"), logout: $("logout"), title: $("chat-title"),
        status: $("connection-status"), banner: $("connection-banner"),
        messages: $("messages"), welcome: $("welcome"),
        composer: $("composer"), input: $("message-input"), send: $("send"), toasts: $("toasts")
    };

    const state = {
        auth: null,            // { accessToken, expiresAtUtc, user }
        authMode: "login",
        conversations: [],
        activeId: null,
        sending: false,
        connection: null,
        stream: null           // { conversationId, text, node, renderPending }
    };

    // ---------- Utilities ----------

    const formatTime = (iso) => {
        const date = new Date(iso);
        const now = new Date();
        const time = date.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
        return date.toDateString() === now.toDateString()
            ? time
            : `${date.toLocaleDateString([], { month: "short", day: "numeric" })}, ${time}`;
    };

    function toast(message, kind = "error", timeout = 5000) {
        const node = document.createElement("div");
        node.className = `toast ${kind}`;
        node.textContent = message;
        el.toasts.appendChild(node);
        setTimeout(() => node.remove(), timeout);
    }

    class ApiError extends Error {
        constructor(status, problem) {
            const fieldErrors = problem?.errors ? Object.values(problem.errors).flat() : [];
            super(fieldErrors.length ? fieldErrors.join("\n") : (problem?.detail || problem?.title || `Request failed (${status})`));
            this.status = status;
            this.problem = problem;
        }
    }

    async function api(path, { method = "GET", body, auth = true } = {}) {
        const headers = { "Accept": "application/json" };
        if (body !== undefined) headers["Content-Type"] = "application/json";
        if (auth && state.auth) headers["Authorization"] = `Bearer ${state.auth.accessToken}`;

        let response;
        try {
            response = await fetch(`${API}${path}`, { method, headers, body: body !== undefined ? JSON.stringify(body) : undefined });
        } catch {
            throw new ApiError(0, { detail: "Cannot reach the server. Check your connection and try again." });
        }

        if (response.status === 401 && auth) {
            signOut("Your session has expired. Please sign in again.");
            throw new ApiError(401, { detail: "Session expired." });
        }
        if (response.status === 204) return null;

        const isJson = (response.headers.get("content-type") || "").includes("json");
        const payload = isJson ? await response.json() : null;
        if (!response.ok) throw new ApiError(response.status, payload);
        return payload;
    }

    // ---------- Markdown ----------

    marked.setOptions({ gfm: true, breaks: true });

    function renderMarkdown(target, text) {
        target.innerHTML = DOMPurify.sanitize(marked.parse(text));

        target.querySelectorAll("a").forEach((a) => { a.target = "_blank"; a.rel = "noopener noreferrer"; });

        target.querySelectorAll("pre > code").forEach((code) => {
            const pre = code.parentElement;
            const language = (code.className.match(/language-([\w+#-]+)/) || [])[1] || "";
            if (language && hljs.getLanguage(language)) {
                code.innerHTML = hljs.highlight(code.textContent, { language }).value;
            } else {
                code.innerHTML = hljs.highlightAuto(code.textContent).value;
            }
            code.classList.add("hljs");

            const wrapper = document.createElement("div");
            wrapper.className = "code-block";
            const header = document.createElement("div");
            header.className = "code-header";
            const label = document.createElement("span");
            label.textContent = language || "code";
            const copy = document.createElement("button");
            copy.type = "button";
            copy.className = "copy-btn";
            copy.textContent = "Copy";
            copy.addEventListener("click", async () => {
                try {
                    await navigator.clipboard.writeText(code.textContent);
                    copy.textContent = "Copied";
                    setTimeout(() => (copy.textContent = "Copy"), 1500);
                } catch {
                    toast("Could not copy to clipboard.");
                }
            });
            header.append(label, copy);
            pre.replaceWith(wrapper);
            wrapper.append(header, pre);
        });
    }

    // ---------- Rendering ----------

    function scrollToBottom(force = false) {
        const m = el.messages;
        const nearBottom = m.scrollHeight - m.scrollTop - m.clientHeight < 160;
        if (force || nearBottom) m.scrollTop = m.scrollHeight;
    }

    function createMessageNode(role, content, createdAtUtc, model) {
        const node = document.createElement("div");
        node.className = `message ${role}`;
        const bubble = document.createElement("div");
        bubble.className = "bubble";
        const meta = document.createElement("div");
        meta.className = "meta";

        if (role === "assistant") {
            bubble.classList.add("markdown");
            if (content) renderMarkdown(bubble, content);
        } else {
            bubble.textContent = content;
        }
        if (createdAtUtc) meta.textContent = formatTime(createdAtUtc) + (model && role === "assistant" ? ` · ${model}` : "");

        node.append(bubble, meta);
        return node;
    }

    function appendMessage(role, content, createdAtUtc, model) {
        el.welcome.hidden = true;
        const node = createMessageNode(role, content, createdAtUtc, model);
        el.messages.appendChild(node);
        scrollToBottom(true);
        return node;
    }

    function showTypingIndicator(node) {
        const bubble = node.querySelector(".bubble");
        bubble.innerHTML = '<span class="typing" aria-label="Assistant is typing"><span></span><span></span><span></span></span>';
    }

    function clearMessages() {
        el.messages.querySelectorAll(".message").forEach((n) => n.remove());
        el.welcome.hidden = false;
    }

    function renderConversationList() {
        el.list.replaceChildren();
        el.listEmpty.hidden = state.conversations.length > 0;

        for (const conversation of state.conversations) {
            const item = document.createElement("li");
            item.className = "conversation-item" + (conversation.id === state.activeId ? " active" : "");
            item.title = conversation.title;

            const title = document.createElement("span");
            title.className = "title";
            title.textContent = conversation.title;

            const del = document.createElement("button");
            del.type = "button";
            del.className = "icon-btn delete";
            del.setAttribute("aria-label", `Delete conversation ${conversation.title}`);
            del.innerHTML = '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 7h16M10 11v6M14 11v6M6 7l1 12a2 2 0 002 2h6a2 2 0 002-2l1-12M9 7V4h6v3"/></svg>';
            del.addEventListener("click", (e) => { e.stopPropagation(); deleteConversation(conversation); });

            item.append(title, del);
            item.addEventListener("click", () => openConversation(conversation.id));
            el.list.appendChild(item);
        }
    }

    function setTitle(text) {
        el.title.textContent = text;
        document.title = text === "New conversation" ? "Chatbot" : `${text} · Chatbot`;
    }

    function updateSendState() {
        const text = el.input.value.trim();
        el.send.disabled = state.sending || text.length === 0 || text.length > MAX_MESSAGE_LENGTH;
    }

    function autoGrow() {
        el.input.style.height = "auto";
        el.input.style.height = Math.min(el.input.scrollHeight, 200) + "px";
    }

    function toggleSidebar(open) {
        el.sidebar.classList.toggle("open", open);
        el.backdrop.hidden = !open;
    }

    // ---------- Conversations ----------

    async function loadConversations() {
        try {
            state.conversations = await api("/api/conversations");
            renderConversationList();
        } catch (err) {
            if (err.status !== 401) toast(err.message);
        }
    }

    async function openConversation(id) {
        if (state.sending) { toast("Please wait for the current response to finish.", "info"); return; }
        toggleSidebar(false);
        state.activeId = id;
        renderConversationList();
        clearMessages();
        el.welcome.hidden = true;

        try {
            const detail = await api(`/api/conversations/${id}`);
            setTitle(detail.title);
            clearMessages();
            for (const m of detail.messages) appendMessage(m.role, m.content, m.createdAtUtc, m.model);
            if (detail.messages.length === 0) el.welcome.hidden = false;
        } catch (err) {
            if (err.status === 404) {
                toast("That conversation no longer exists.");
                state.activeId = null;
                await loadConversations();
                startNewConversation();
            } else if (err.status !== 401) {
                toast(err.message);
            }
        }
    }

    function startNewConversation() {
        if (state.sending) { toast("Please wait for the current response to finish.", "info"); return; }
        state.activeId = null;
        setTitle("New conversation");
        clearMessages();
        renderConversationList();
        toggleSidebar(false);
        el.input.focus();
    }

    async function deleteConversation(conversation) {
        if (!window.confirm(`Delete "${conversation.title}"? This cannot be undone.`)) return;
        try {
            await api(`/api/conversations/${conversation.id}`, { method: "DELETE" });
            state.conversations = state.conversations.filter((c) => c.id !== conversation.id);
            if (state.activeId === conversation.id) startNewConversation();
            renderConversationList();
            toast("Conversation deleted.", "success", 2500);
        } catch (err) {
            if (err.status !== 401) toast(err.message);
        }
    }

    function upsertConversation(id, title) {
        const now = new Date().toISOString();
        const existing = state.conversations.find((c) => c.id === id);
        if (existing) {
            existing.title = title;
            existing.updatedAtUtc = now;
            state.conversations = [existing, ...state.conversations.filter((c) => c.id !== id)];
        } else {
            state.conversations.unshift({ id, title, createdAtUtc: now, updatedAtUtc: now });
        }
        renderConversationList();
    }

    // ---------- Real-time connection (SignalR) ----------

    function setConnectionStatus(kind, bannerText) {
        el.status.className = `connection-status ${kind}`;
        el.status.title = { connected: "Connected", reconnecting: "Reconnecting…", disconnected: "Offline" }[kind] || "";
        el.banner.hidden = !bannerText;
        el.banner.textContent = bannerText || "";
        el.banner.classList.toggle("error", kind === "disconnected");
    }

    function buildConnection() {
        const connection = new signalR.HubConnectionBuilder()
            .withUrl(`${API}/hubs/chat`, { accessTokenFactory: () => state.auth?.accessToken ?? "" })
            .withAutomaticReconnect([0, 2000, 5000, 10000, 20000, 30000])
            .configureLogging(signalR.LogLevel.Warning)
            .build();

        connection.on("UserMessageSaved", (conversationId, conversationTitle) => {
            if (!state.stream) return;
            state.stream.conversationId = conversationId;
            if (state.activeId === null) state.activeId = conversationId;
            if (state.activeId === conversationId) setTitle(conversationTitle);
            upsertConversation(conversationId, conversationTitle);
        });

        connection.on("ReceiveDelta", (conversationId, text) => {
            const stream = state.stream;
            if (!stream || stream.conversationId !== conversationId) return;
            stream.text += text;
            if (!stream.renderPending) {
                stream.renderPending = true;
                requestAnimationFrame(() => {
                    stream.renderPending = false;
                    if (state.stream === stream) {
                        renderMarkdown(stream.node.querySelector(".bubble"), stream.text);
                        scrollToBottom();
                    }
                });
            }
        });

        connection.onreconnecting(() => setConnectionStatus("reconnecting", "Connection lost. Reconnecting…"));
        connection.onreconnected(() => {
            setConnectionStatus("connected");
            toast("Reconnected.", "success", 2000);
            loadConversations();
        });
        connection.onclose(() => {
            if (!state.auth) return;
            setConnectionStatus("disconnected", "Real-time connection unavailable. Messages will be sent without live streaming.");
            setTimeout(startConnection, 10000);
        });

        return connection;
    }

    async function startConnection() {
        if (!state.auth) return;
        if (!state.connection) state.connection = buildConnection();
        if (state.connection.state !== signalR.HubConnectionState.Disconnected) return;

        try {
            await state.connection.start();
            setConnectionStatus("connected");
        } catch {
            setConnectionStatus("disconnected", "Real-time connection unavailable. Retrying…");
            setTimeout(startConnection, 5000);
        }
    }

    async function stopConnection() {
        const connection = state.connection;
        state.connection = null;
        if (connection) {
            try { await connection.stop(); } catch { /* ignore */ }
        }
    }

    // ---------- Sending ----------

    async function sendMessage() {
        const text = el.input.value.trim();
        if (!text || state.sending) return;
        if (text.length > MAX_MESSAGE_LENGTH) { toast(`Messages are limited to ${MAX_MESSAGE_LENGTH} characters.`); return; }

        state.sending = true;
        updateSendState();
        el.input.value = "";
        autoGrow();

        const userNode = appendMessage("user", text, new Date().toISOString());
        const assistantNode = appendMessage("assistant", "");
        showTypingIndicator(assistantNode);
        state.stream = { conversationId: state.activeId, text: "", node: assistantNode, renderPending: false };

        const request = { conversationId: state.activeId, message: text };

        try {
            const useHub = state.connection?.state === signalR.HubConnectionState.Connected;
            const response = useHub
                ? await state.connection.invoke("SendMessage", request)
                : await api("/api/chat", { method: "POST", body: request });

            state.activeId = response.conversationId;
            setTitle(response.conversationTitle);
            upsertConversation(response.conversationId, response.conversationTitle);

            const finalNode = createMessageNode("assistant", response.assistantMessage.content,
                response.assistantMessage.createdAtUtc, response.assistantMessage.model);
            assistantNode.replaceWith(finalNode);
            userNode.querySelector(".meta").textContent = formatTime(response.userMessage.createdAtUtc);
            scrollToBottom();
        } catch (err) {
            const message = cleanHubError(err);
            const partial = state.stream?.text;
            if (partial) {
                renderMarkdown(assistantNode.querySelector(".bubble"), partial);
            } else {
                assistantNode.querySelector(".bubble").textContent = "No response received.";
            }
            assistantNode.classList.add("failed");
            assistantNode.querySelector(".meta").textContent = message;
            if (err.status !== 401) toast(message);

            // A new conversation may have been created before the failure.
            if (state.stream?.conversationId && !state.activeId) state.activeId = state.stream.conversationId;
        } finally {
            state.stream = null;
            state.sending = false;
            updateSendState();
            el.input.focus();
        }
    }

    function cleanHubError(err) {
        if (err instanceof ApiError) return err.message;
        const raw = String(err?.message || err || "Something went wrong.");
        // SignalR prefixes server errors with transport details.
        const match = raw.match(/HubException: (.*)$/s);
        return match ? match[1] : raw.replace(/^An unexpected error occurred invoking '\w+' on the server\.?\s*/, "") || "Something went wrong. Please try again.";
    }

    // ---------- Authentication ----------

    function loadStoredAuth() {
        try {
            const stored = JSON.parse(localStorage.getItem(AUTH_KEY) || "null");
            if (stored && new Date(stored.expiresAtUtc).getTime() > Date.now() + 30_000) return stored;
        } catch { /* ignore */ }
        localStorage.removeItem(AUTH_KEY);
        return null;
    }

    function setAuthMode(mode) {
        state.authMode = mode;
        document.querySelectorAll(".auth-tab").forEach((tab) => {
            const active = tab.dataset.mode === mode;
            tab.classList.toggle("active", active);
            tab.setAttribute("aria-selected", String(active));
        });
        document.querySelectorAll(".register-only").forEach((n) => (n.hidden = mode !== "register"));
        el.authSubmit.textContent = mode === "register" ? "Create account" : "Sign in";
        el.authForm.password.autocomplete = mode === "register" ? "new-password" : "current-password";
        el.authError.hidden = true;
    }

    async function submitAuth(event) {
        event.preventDefault();
        el.authError.hidden = true;
        const form = el.authForm;
        const body = { email: form.email.value.trim(), password: form.password.value };
        if (state.authMode === "register") body.displayName = form.displayName.value.trim() || null;

        el.authSubmit.disabled = true;
        try {
            const path = state.authMode === "register" ? "/api/auth/register" : "/api/auth/login";
            const auth = await api(path, { method: "POST", body, auth: false });
            localStorage.setItem(AUTH_KEY, JSON.stringify(auth));
            form.reset();
            await enterChat(auth);
        } catch (err) {
            el.authError.textContent = err.message;
            el.authError.hidden = false;
        } finally {
            el.authSubmit.disabled = false;
        }
    }

    async function enterChat(auth) {
        state.auth = auth;
        el.userName.textContent = auth.user.displayName || auth.user.email;
        el.authView.hidden = true;
        el.chatView.hidden = false;
        startNewConversation();
        await Promise.all([loadConversations(), startConnection()]);
        scheduleExpiry();
    }

    let expiryTimer = null;
    function scheduleExpiry() {
        clearTimeout(expiryTimer);
        const ms = new Date(state.auth.expiresAtUtc).getTime() - Date.now();
        expiryTimer = setTimeout(() => signOut("Your session has expired. Please sign in again."), Math.max(ms, 0));
    }

    function signOut(message) {
        clearTimeout(expiryTimer);
        localStorage.removeItem(AUTH_KEY);
        state.auth = null;
        state.conversations = [];
        state.activeId = null;
        stopConnection();
        el.chatView.hidden = true;
        el.authView.hidden = false;
        setAuthMode("login");
        if (message) toast(message, "info");
    }

    // ---------- Wire-up ----------

    document.querySelectorAll(".auth-tab").forEach((tab) => tab.addEventListener("click", () => setAuthMode(tab.dataset.mode)));
    el.authForm.addEventListener("submit", submitAuth);
    el.logout.addEventListener("click", () => signOut());
    el.newChat.addEventListener("click", startNewConversation);
    $("open-sidebar").addEventListener("click", () => toggleSidebar(true));
    $("close-sidebar").addEventListener("click", () => toggleSidebar(false));
    el.backdrop.addEventListener("click", () => toggleSidebar(false));

    el.composer.addEventListener("submit", (e) => { e.preventDefault(); sendMessage(); });
    el.input.addEventListener("input", () => { autoGrow(); updateSendState(); });
    el.input.addEventListener("keydown", (e) => {
        if (e.key === "Enter" && !e.shiftKey && !e.isComposing) {
            e.preventDefault();
            sendMessage();
        }
    });

    const stored = loadStoredAuth();
    if (stored) {
        enterChat(stored);
    } else {
        el.authView.hidden = false;
        setAuthMode("login");
    }
})();
