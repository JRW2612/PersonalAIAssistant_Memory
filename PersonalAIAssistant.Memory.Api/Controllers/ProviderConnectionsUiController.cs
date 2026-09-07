using Microsoft.AspNetCore.Mvc;

namespace PersonalAIAssistant.Memory.Api.Controllers
{
    [ApiController]
    [ApiExplorerSettings(IgnoreApi = true)]
    public class ProviderConnectionsUiController : ControllerBase
    {
        [HttpGet("/connections")]
        [Produces("text/html")]
        public ContentResult GetDashboard()
        {
            var html = """
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="UTF-8">
                <meta name="viewport" content="width=device-width, initial-scale=1.0">
                <title>Provider Connections — Personal AI Assistant</title>
                <style>
                    :root {
                        --bg: #0b0f19;
                        --card-bg: rgba(22, 30, 46, 0.85);
                        --border: rgba(255, 255, 255, 0.08);
                        --primary: #3b82f6;
                        --primary-hover: #2563eb;
                        --success: #10b981;
                        --danger: #ef4444;
                        --warning: #f59e0b;
                        --text: #f3f4f6;
                        --text-muted: #9ca3af;
                    }
                    * { box-sizing: border-box; margin: 0; padding: 0; font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Oxygen, Ubuntu, sans-serif; }
                    body { background: var(--bg); color: var(--text); padding: 2rem 1.5rem; min-height: 100vh; }
                    .container { max-width: 1100px; margin: 0 auto; }
                    header { margin-bottom: 2rem; border-bottom: 1px solid var(--border); padding-bottom: 1.5rem; display: flex; justify-content: space-between; align-items: center; flex-wrap: wrap; gap: 1rem; }
                    h1 { font-size: 1.875rem; font-weight: 700; background: linear-gradient(to right, #60a5fa, #a78bfa); -webkit-background-clip: text; -webkit-text-fill-color: transparent; }
                    p.subtitle { color: var(--text-muted); font-size: 0.95rem; margin-top: 0.25rem; }
                    .user-badge { background: rgba(59, 130, 246, 0.15); border: 1px solid rgba(59, 130, 246, 0.3); color: #93c5fd; padding: 0.4rem 0.8rem; border-radius: 9999px; font-size: 0.85rem; }
                    
                    /* Alert */
                    .alert { padding: 1rem 1.25rem; border-radius: 8px; margin-bottom: 2rem; display: none; font-size: 0.95rem; }
                    .alert-success { background: rgba(16, 185, 129, 0.15); border: 1px solid var(--success); color: #6ee7b7; }
                    .alert-danger { background: rgba(239, 68, 68, 0.15); border: 1px solid var(--danger); color: #fca5a5; }

                    /* Grid */
                    .grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(320px, 1fr)); gap: 1.5rem; margin-bottom: 2.5rem; }
                    .card { background: var(--card-bg); border: 1px solid var(--border); border-radius: 12px; padding: 1.5rem; backdrop-filter: blur(12px); display: flex; flex-direction: column; justify-content: space-between; position: relative; }
                    .card-header { display: flex; align-items: center; justify-content: space-between; margin-bottom: 1rem; }
                    .provider-title { display: flex; align-items: center; gap: 0.75rem; font-size: 1.25rem; font-weight: 600; }
                    .badge { font-size: 0.75rem; font-weight: 600; padding: 0.25rem 0.6rem; border-radius: 9999px; text-transform: uppercase; letter-spacing: 0.05em; }
                    .badge-active { background: rgba(16, 185, 129, 0.2); color: #34d399; border: 1px solid rgba(16, 185, 129, 0.4); }
                    .badge-notconnected { background: rgba(156, 163, 175, 0.15); color: #9ca3af; border: 1px solid rgba(156, 163, 175, 0.2); }
                    .badge-revoked { background: rgba(239, 68, 68, 0.15); color: #f87171; border: 1px solid rgba(239, 68, 68, 0.3); }

                    .provider-desc { color: var(--text-muted); font-size: 0.875rem; margin-bottom: 1.25rem; min-height: 2.5rem; }
                    .meta-field { font-size: 0.825rem; margin-bottom: 0.5rem; color: #d1d5db; display: flex; justify-content: space-between; }
                    .meta-label { color: var(--text-muted); }
                    
                    /* Scopes checklist */
                    .scopes-container { margin: 1rem 0; background: rgba(0, 0, 0, 0.25); border-radius: 8px; padding: 0.75rem; }
                    .scopes-title { font-size: 0.8rem; font-weight: 600; color: var(--text-muted); margin-bottom: 0.5rem; text-transform: uppercase; }
                    .scope-item { display: flex; align-items: center; gap: 0.5rem; font-size: 0.825rem; margin-bottom: 0.35rem; color: #e5e7eb; }
                    .scope-item input { accent-color: var(--primary); }

                    /* Buttons */
                    .btn-group { display: flex; flex-direction: column; gap: 0.5rem; margin-top: 1.25rem; }
                    .btn { display: inline-flex; align-items: center; justify-content: center; gap: 0.5rem; padding: 0.6rem 1rem; border-radius: 6px; font-weight: 500; font-size: 0.875rem; cursor: pointer; border: none; transition: all 0.2s; text-decoration: none; text-align: center; }
                    .btn-primary { background: var(--primary); color: white; }
                    .btn-primary:hover { background: var(--primary-hover); }
                    .btn-secondary { background: rgba(255, 255, 255, 0.08); color: var(--text); border: 1px solid var(--border); }
                    .btn-secondary:hover { background: rgba(255, 255, 255, 0.14); }
                    .btn-danger { background: rgba(239, 68, 68, 0.15); color: #fca5a5; border: 1px solid rgba(239, 68, 68, 0.3); }
                    .btn-danger:hover { background: rgba(239, 68, 68, 0.25); }

                    /* Audit Log */
                    .audit-section { margin-top: 3rem; }
                    .section-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 1rem; }
                    .section-title { font-size: 1.25rem; font-weight: 600; }
                    table { width: 100%; border-collapse: collapse; background: var(--card-bg); border-radius: 8px; overflow: hidden; border: 1px solid var(--border); font-size: 0.875rem; }
                    th, td { padding: 0.85rem 1rem; text-align: left; border-bottom: 1px solid var(--border); }
                    th { background: rgba(0, 0, 0, 0.35); font-weight: 600; color: var(--text-muted); font-size: 0.8rem; text-transform: uppercase; }
                    tr:hover { background: rgba(255, 255, 255, 0.02); }

                    /* Direct Key Modal */
                    .modal { display: none; position: fixed; inset: 0; background: rgba(0,0,0,0.7); backdrop-filter: blur(4px); align-items: center; justify-content: center; z-index: 100; }
                    .modal.show { display: flex; }
                    .modal-content { background: #161e2e; border: 1px solid var(--border); border-radius: 12px; width: 90%; max-width: 480px; padding: 1.5rem; }
                    .modal-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 1rem; }
                    .modal-close { background: none; border: none; color: var(--text-muted); font-size: 1.25rem; cursor: pointer; }
                    .form-group { margin-bottom: 1rem; }
                    .form-label { display: block; font-size: 0.85rem; color: var(--text-muted); margin-bottom: 0.35rem; }
                    .form-control { width: 100%; background: #0b0f19; border: 1px solid var(--border); border-radius: 6px; padding: 0.6rem 0.75rem; color: var(--text); font-size: 0.9rem; }
                    .form-control:focus { outline: none; border-color: var(--primary); }
                </style>
            </head>
            <body>
                <div class="container">
                    <header>
                        <div>
                            <h1>Connect My Personal AI Account</h1>
                            <p class="subtitle">Bring Your Own Key (BYOK) & OAuth 2.0 with AES-256-GCM encrypted token storage.</p>
                        </div>
                        <div class="user-badge" id="userBadge">User: user-alice</div>
                    </header>

                    <div id="alertBox" class="alert"></div>

                    <div class="grid" id="providersGrid">
                        <!-- Provider cards dynamically rendered here -->
                    </div>

                    <div class="audit-section">
                        <div class="section-header">
                            <h2 class="section-title">Provider Audit Trail (Tamper-Evident Hash Chain)</h2>
                            <button class="btn btn-secondary" onclick="loadAuditLogs()">Refresh Logs</button>
                        </div>
                        <table>
                            <thead>
                                <tr>
                                    <th>Timestamp (UTC)</th>
                                    <th>Provider</th>
                                    <th>Event</th>
                                    <th>Details</th>
                                </tr>
                            </thead>
                            <tbody id="auditTableBody">
                                <tr><td colspan="4" style="text-align:center; color: var(--text-muted);">Loading audit records...</td></tr>
                            </tbody>
                        </table>
                    </div>
                </div>

                <!-- BYOK Modal -->
                <div class="modal" id="byokModal">
                    <div class="modal-content">
                        <div class="modal-header">
                            <h3 id="modalTitle">Connect API Key</h3>
                            <button class="modal-close" onclick="closeModal()">&times;</button>
                        </div>
                        <div class="form-group">
                            <label class="form-label">API Key / Access Token</label>
                            <input type="password" id="modalKeyInput" class="form-control" placeholder="Paste your API key or token here" />
                        </div>
                        <div class="form-group">
                            <label class="form-label">Account Name / Email (Optional)</label>
                            <input type="text" id="modalAccountInput" class="form-control" placeholder="e.g. personal-gemini@example.com" />
                        </div>
                        <div class="btn-group">
                            <button class="btn btn-primary" onclick="submitDirectConnect()">Encrypt & Save Connection</button>
                            <button class="btn btn-secondary" onclick="closeModal()">Cancel</button>
                        </div>
                    </div>
                </div>

                <script>
                    const params = new URLSearchParams(window.location.search);
                    let currentProviderForModal = '';

                    window.onload = function() {
                        const status = params.get('status');
                        const provider = params.get('provider');
                        const message = params.get('message');

                        if (status === 'connected') {
                            showAlert(`Successfully connected ${provider.toUpperCase()} account via OAuth 2.0! Encrypted tokens saved.`, 'success');
                        } else if (status === 'error') {
                            showAlert(`Connection error: ${message || 'Failed to authenticate.'}`, 'danger');
                        }

                        loadConnections();
                        loadAuditLogs();
                    };

                    function showAlert(msg, type) {
                        const box = document.getElementById('alertBox');
                        box.className = `alert alert-${type}`;
                        box.innerText = msg;
                        box.style.display = 'block';
                        setTimeout(() => { box.style.display = 'none'; }, 8000);
                    }

                    async function loadConnections() {
                        try {
                            const res = await fetch('/api/v1/connections');
                            const providers = await res.json();
                            const grid = document.getElementById('providersGrid');
                            grid.innerHTML = '';

                            providers.forEach(p => {
                                const card = document.createElement('div');
                                card.className = 'card';

                                const isActive = p.isActive;
                                const badgeClass = isActive ? 'badge-active' : (p.status === 'Revoked' ? 'badge-revoked' : 'badge-notconnected');
                                const statusLabel = isActive ? 'Connected' : (p.status === 'Revoked' ? 'Revoked' : 'Not Connected');

                                const providerDescriptions = {
                                    'gemini': 'Google Gemini 1.5 Flash / Pro. Used for memory compression, semantic consolidation, and chat synthesis.',
                                    'openai': 'OpenAI GPT-4o and Text-Embedding-3. Used for vector embedding generation and chat.',
                                    'anthropic': 'Anthropic Claude 3.5 Sonnet. Advanced reasoning and contextual recall.'
                                };

                                card.innerHTML = `
                                    <div>
                                        <div class="card-header">
                                            <div class="provider-title">
                                                <span>${p.provider.toUpperCase()}</span>
                                            </div>
                                            <span class="badge ${badgeClass}">${statusLabel}</span>
                                        </div>
                                        <p class="provider-desc">${providerDescriptions[p.provider.toLowerCase()] || 'AI Provider connection.'}</p>
                                        
                                        ${isActive ? `
                                            <div class="meta-field">
                                                <span class="meta-label">Credential Kind:</span>
                                                <span>${p.credentialKind}</span>
                                            </div>
                                            <div class="meta-field">
                                                <span class="meta-label">Account:</span>
                                                <span>${p.accountEmail || p.accountName || 'Active Session'}</span>
                                            </div>
                                            <div class="meta-field">
                                                <span class="meta-label">Storage:</span>
                                                <span style="color: #34d399;">AES-256-GCM Encrypted</span>
                                            </div>
                                            <div class="meta-field">
                                                <span class="meta-label">Scopes:</span>
                                                <span>${(p.consentScopes && p.consentScopes.length) ? p.consentScopes.join(', ') : 'ai.generate'}</span>
                                            </div>
                                        ` : `
                                            <div class="scopes-container">
                                                <div class="scopes-title">Consent Scopes:</div>
                                                <label class="scope-item"><input type="checkbox" id="${p.provider}-s1" checked disabled /> ai.generate (Model Inference)</label>
                                                <label class="scope-item"><input type="checkbox" id="${p.provider}-s2" checked /> ai.memory.read (Context Injection)</label>
                                                <label class="scope-item"><input type="checkbox" id="${p.provider}-s3" checked /> ai.memory.write (Consolidation Save)</label>
                                            </div>
                                        `}
                                    </div>

                                    <div class="btn-group">
                                        ${isActive ? `
                                            <button class="btn btn-primary" onclick="testConnection('${p.provider}')">Test Connectivity</button>
                                            <button class="btn btn-danger" onclick="revokeConnection('${p.provider}')">Disconnect / Revoke</button>
                                        ` : `
                                            ${p.provider.toLowerCase() === 'gemini' ? `
                                                <a class="btn btn-primary" href="/api/v1/connections/gemini/authorize?redirect=true">
                                                    Connect with Google OAuth 2.0
                                                </a>
                                            ` : ''}
                                            <button class="btn btn-secondary" onclick="openByokModal('${p.provider}')">
                                                Connect with API Key (BYOK)
                                            </button>
                                        `}
                                    </div>
                                `;
                                grid.appendChild(card);
                            });
                        } catch (err) {
                            console.error('Failed to load connections:', err);
                        }
                    }

                    async function loadAuditLogs() {
                        try {
                            const res = await fetch('/api/v1/connections/audit-logs?limit=15');
                            const logs = await res.json();
                            const tbody = document.getElementById('auditTableBody');
                            tbody.innerHTML = '';

                            if (!logs || logs.length === 0) {
                                tbody.innerHTML = '<tr><td colspan="4" style="text-align:center; color: var(--text-muted);">No connection audit records yet.</td></tr>';
                                return;
                            }

                            logs.forEach(l => {
                                const row = document.createElement('tr');
                                const eventColor = l.eventType === 'CONNECTED' ? '#34d399' : (l.eventType === 'REVOKED' ? '#f87171' : '#60a5fa');
                                row.innerHTML = `
                                    <td style="white-space: nowrap; color: var(--text-muted);">${new Date(l.timestampUtc).toLocaleString()}</td>
                                    <td><strong>${l.provider.toUpperCase()}</strong></td>
                                    <td><span style="color: ${eventColor}; font-weight: 600;">${l.eventType}</span></td>
                                    <td>${l.details}</td>
                                `;
                                tbody.appendChild(row);
                            });
                        } catch (err) {
                            console.error('Failed to load audit logs:', err);
                        }
                    }

                    function openByokModal(provider) {
                        currentProviderForModal = provider;
                        document.getElementById('modalTitle').innerText = `Connect ${provider.toUpperCase()} API Key`;
                        document.getElementById('modalKeyInput').value = '';
                        document.getElementById('modalAccountInput').value = '';
                        document.getElementById('byokModal').classList.add('show');
                    }

                    function closeModal() {
                        document.getElementById('byokModal').classList.remove('show');
                    }

                    async function submitDirectConnect() {
                        const key = document.getElementById('modalKeyInput').value.trim();
                        const account = document.getElementById('modalAccountInput').value.trim();

                        if (!key) {
                            alert('Please provide an API key or token.');
                            return;
                        }

                        try {
                            const res = await fetch(`/api/v1/connections/${currentProviderForModal}/connect`, {
                                method: 'POST',
                                headers: { 'Content-Type': 'application/json' },
                                body: JSON.stringify({
                                    apiKey: key,
                                    accountName: account || `${currentProviderForModal}-key`,
                                    scopes: ['ai.generate', 'ai.memory.read', 'ai.memory.write']
                                })
                            });

                            if (res.ok) {
                                closeModal();
                                showAlert(`Successfully connected ${currentProviderForModal.toUpperCase()}! Credentials encrypted and stored.`, 'success');
                                loadConnections();
                                loadAuditLogs();
                            } else {
                                const err = await res.text();
                                alert('Error connecting: ' + err);
                            }
                        } catch (err) {
                            alert('Failed to connect: ' + err.message);
                        }
                    }

                    async function revokeConnection(provider) {
                        if (!confirm(`Are you sure you want to disconnect and revoke your ${provider.toUpperCase()} connection? Stored credentials will be purged.`)) {
                            return;
                        }

                        try {
                            const res = await fetch(`/api/v1/connections/${provider}/revoke`, {
                                method: 'DELETE',
                                headers: { 'Content-Type': 'application/json' },
                                body: JSON.stringify({ reason: 'User clicked disconnect on Provider Connections dashboard' })
                            });

                            if (res.ok) {
                                showAlert(`Connection for ${provider.toUpperCase()} has been revoked and credentials purged.`, 'success');
                                loadConnections();
                                loadAuditLogs();
                            } else {
                                alert('Failed to revoke connection.');
                            }
                        } catch (err) {
                            alert('Error revoking connection: ' + err.message);
                        }
                    }

                    async function testConnection(provider) {
                        showAlert(`Testing connectivity with ${provider.toUpperCase()}...`, 'success');
                        try {
                            const res = await fetch(`/api/v1/connections/${provider}/test`, { method: 'POST' });
                            const data = await res.json();
                            if (data.success) {
                                showAlert(`Connectivity OK (${data.responseTimeMs}ms): ${data.message}`, 'success');
                            } else {
                                showAlert(`Connectivity Failed (${data.responseTimeMs}ms): ${data.message}`, 'danger');
                            }
                            loadAuditLogs();
                        } catch (err) {
                            showAlert(`Connectivity Error: ${err.message}`, 'danger');
                        }
                    }
                </script>
            </body>
            </html>
            """;

            return Content(html, "text/html");
        }
    }
}
