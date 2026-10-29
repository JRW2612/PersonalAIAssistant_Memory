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
                <title>Provider Connections & Security — Personal AI Assistant</title>
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
                    
                    /* Navigation Bar */
                    nav.navbar { display: flex; align-items: center; justify-content: space-between; border-bottom: 1px solid var(--border); padding-bottom: 1rem; margin-bottom: 2rem; }
                    .nav-links { display: flex; gap: 1rem; }
                    .nav-link { color: var(--text-muted); text-decoration: none; font-size: 0.9rem; font-weight: 500; padding: 0.4rem 0.8rem; border-radius: 6px; transition: all 0.2s; }
                    .nav-link:hover, .nav-link.active { color: var(--text); background: rgba(255,255,255,0.06); }
                    .nav-badge-admin { background: rgba(239, 68, 68, 0.15); color: #f87171; border: 1px solid rgba(239, 68, 68, 0.3); font-size: 0.7rem; padding: 0.15rem 0.4rem; border-radius: 4px; margin-left: 0.4rem; text-transform: uppercase; }

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
                    
                    /* Buttons */
                    .btn-group { display: flex; flex-direction: column; gap: 0.5rem; margin-top: 1.25rem; }
                    .btn { display: inline-flex; align-items: center; justify-content: center; gap: 0.5rem; padding: 0.6rem 1rem; border-radius: 6px; font-weight: 500; font-size: 0.875rem; cursor: pointer; border: none; transition: all 0.2s; text-decoration: none; text-align: center; }
                    .btn-primary { background: var(--primary); color: white; }
                    .btn-primary:hover { background: var(--primary-hover); }
                    .btn-secondary { background: rgba(255, 255, 255, 0.08); color: var(--text); border: 1px solid var(--border); }
                    .btn-secondary:hover { background: rgba(255, 255, 255, 0.14); }
                    .btn-danger { background: rgba(239, 68, 68, 0.15); color: #fca5a5; border: 1px solid rgba(239, 68, 68, 0.3); }
                    .btn-danger:hover { background: rgba(239, 68, 68, 0.25); }

                    /* Security Boundary Demonstration Panel */
                    .security-demo-box { background: rgba(30, 41, 59, 0.7); border: 1px solid rgba(96, 165, 250, 0.3); border-radius: 12px; padding: 1.5rem; margin-top: 3rem; margin-bottom: 2rem; }
                    .security-demo-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 1rem; }
                    .security-demo-title { font-size: 1.15rem; font-weight: 600; color: #93c5fd; }
                    .security-demo-desc { font-size: 0.875rem; color: #cbd5e1; margin-bottom: 1.25rem; line-height: 1.5; }
                    .tamper-actions { display: flex; gap: 1rem; flex-wrap: wrap; }

                    /* Audit Log */
                    .audit-section { margin-top: 2rem; }
                    .section-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 1rem; }
                    .section-title { font-size: 1.25rem; font-weight: 600; }
                    table { width: 100%; border-collapse: collapse; background: var(--card-bg); border-radius: 8px; overflow: hidden; border: 1px solid var(--border); font-size: 0.875rem; }
                    th, td { padding: 0.85rem 1rem; text-align: left; border-bottom: 1px solid var(--border); }
                    th { background: rgba(0, 0, 0, 0.35); font-weight: 600; color: var(--text-muted); font-size: 0.8rem; text-transform: uppercase; }
                    tr:hover { background: rgba(255, 255, 255, 0.02); }

                    /* Modal */
                    .modal { display: none; position: fixed; inset: 0; background: rgba(0,0,0,0.7); backdrop-filter: blur(4px); align-items: center; justify-content: center; z-index: 100; }
                    .modal.show { display: flex; }
                    .modal-content { background: #161e2e; border: 1px solid var(--border); border-radius: 12px; width: 90%; max-width: 480px; padding: 1.5rem; }
                    .modal-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 1rem; }
                    .modal-close { background: none; border: none; color: var(--text-muted); font-size: 1.25rem; cursor: pointer; }
                    .form-group { margin-bottom: 1rem; }
                    .form-label { display: block; font-size: 0.85rem; color: var(--text-muted); margin-bottom: 0.35rem; }
                    .form-control { width: 100%; background: #0b0f19; border: 1px solid var(--border); border-radius: 6px; padding: 0.6rem 0.75rem; color: var(--text); font-size: 0.9rem; }
                </style>
            </head>
            <body>
                <div class="container">
                    <!-- Dynamic Role-Based Navigation -->
                    <nav class="navbar">
                        <div class="nav-links" id="dynamicNav">
                            <a href="/memories" class="nav-link">Memories</a>
                            <a href="/connections" class="nav-link active">AI Connections</a>
                        </div>
                        <div class="user-badge" id="userBadge">Resolving Session...</div>
                    </nav>

                    <header>
                        <div>
                            <h1>AI Provider Connections & Security</h1>
                            <p class="subtitle">Role-based UI visibility with authoritative backend security enforcement.</p>
                        </div>
                    </header>

                    <div id="alertBox" class="alert"></div>

                    <!-- AI Connections Grid -->
                    <div class="grid" id="providersGrid">
                        <!-- Provider cards dynamically rendered here -->
                    </div>

                    <!-- Interactive Anti-Tamper & Security Boundary Demonstration -->
                    <div class="security-demo-box">
                        <div class="security-demo-header">
                            <span class="security-demo-title">🛡️ Backend Security Boundary & Anti-Tamper Verification</span>
                            <span class="badge badge-active">Backend Authoritative</span>
                        </div>
                        <p class="security-demo-desc">
                            <strong>Principle:</strong> Frontend UI checks adapt visibility for user experience, but <em>never</em> act as a security boundary.
                            Even if an attacker alters client JavaScript or DevTools to unhide administrative buttons, the backend independently verifies tenant boundaries and roles, rejecting unauthorized calls with <strong>403 Forbidden</strong>.
                        </p>
                        <div class="tamper-actions">
                            <button class="btn btn-danger" onclick="simulateTamperAttempt('hsm.rotate', null, null)">
                                ⚠️ Simulate DOM Tamper (Invoke HSM Key Rotation)
                            </button>
                            <button class="btn btn-danger" onclick="simulateTamperAttempt('memory.read', 'mem-cross-tenant-123', 'tenant-foreign')">
                                ⚠️ Simulate Cross-Tenant BOLA / IDOR Tamper
                            </button>
                        </div>
                    </div>

                    <!-- Audit Log Section (Role-Protected Visibility) -->
                    <div class="audit-section" id="auditSection" style="display: none;">
                        <div class="section-header">
                            <h2 class="section-title">Tamper-Evident Audit Trail (Restricted Visibility)</h2>
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
                    let uiCapabilities = {
                        userId: 'user-alice',
                        tenantId: 'default',
                        canManageConnections: true,
                        canRevokeConnections: false,
                        canViewAuditLogs: false,
                        visibleNavigation: []
                    };
                    let currentProviderForModal = '';

                    window.onload = async function() {
                        await loadUiCapabilities();
                        loadConnections();
                    };

                    async function loadUiCapabilities() {
                        try {
                            const res = await fetch('/api/v1/ui/capabilities');
                            if (res.ok) {
                                uiCapabilities = await res.json();
                                document.getElementById('userBadge').innerText = 
                                    `User: ${uiCapabilities.userId} | Tenant: ${uiCapabilities.tenantId} | Roles: ${uiCapabilities.roles.join(', ') || 'Employee'}`;

                                // Render dynamic navigation
                                if (uiCapabilities.visibleNavigation && uiCapabilities.visibleNavigation.length > 0) {
                                    const navContainer = document.getElementById('dynamicNav');
                                    navContainer.innerHTML = uiCapabilities.visibleNavigation.map(item => {
                                        const adminBadge = item.isAdminOnly ? '<span class="nav-badge-admin">Admin</span>' : '';
                                        const active = item.id === 'connections' ? 'active' : '';
                                        return `<a href="${item.path}" class="nav-link ${active}">${item.label}${adminBadge}</a>`;
                                    }).join('');
                                }

                                // Conditionally display audit logs section based on authoritative backend permission
                                if (uiCapabilities.canViewAuditLogs) {
                                    document.getElementById('auditSection').style.display = 'block';
                                    loadAuditLogs();
                                }
                            }
                        } catch (err) {
                            console.warn('Could not load authoritative UI capabilities:', err);
                        }
                    }

                    function showAlert(msg, type) {
                        const box = document.getElementById('alertBox');
                        box.className = `alert alert-${type}`;
                        box.innerText = msg;
                        box.style.display = 'block';
                        setTimeout(() => { box.style.display = 'none'; }, 8000);
                    }

                    async function loadConnections() {
                        const grid = document.getElementById('providersGrid');
                        try {
                            const res = await fetch('/api/v1/connections');
                            if (!res.ok) {
                                grid.innerHTML = '<p style="color:var(--text-muted)">Failed to load connections.</p>';
                                return;
                            }
                            const data = await res.json();
                            grid.innerHTML = data.map(p => renderProviderCard(p)).join('');
                        } catch (err) {
                            grid.innerHTML = `<p style="color:var(--danger)">Error: ${err.message}</p>`;
                        }
                    }

                    function renderProviderCard(p) {
                        const badgeClass = p.status === 'Active' ? 'badge-active' : (p.status === 'Revoked' ? 'badge-revoked' : 'badge-notconnected');
                        
                        // Role-based button visibility
                        const disconnectBtn = (p.status === 'Active' && uiCapabilities.canRevokeConnections)
                            ? `<button class="btn btn-danger" onclick="revokeConnection('${p.provider}')">Disconnect & Purge Key</button>`
                            : '';

                        const connectBtn = uiCapabilities.canManageConnections
                            ? `<button class="btn btn-primary" onclick="openByokModal('${p.provider}')">Connect API Key (BYOK)</button>`
                            : `<span style="font-size:0.8rem; color:var(--text-muted); text-align:center;">Connection management restricted to Admin</span>`;

                        return `
                        <div class="card">
                            <div>
                                <div class="card-header">
                                    <div class="provider-title">${p.provider.toUpperCase()}</div>
                                    <span class="badge ${badgeClass}">${p.status}</span>
                                </div>
                                <div class="provider-desc">${p.description}</div>
                                <div class="meta-field">
                                    <span class="meta-label">Encrypted DEK Format</span>
                                    <span>${p.encryptedKeyFormat || 'FIPS 140-2 Level 3 (0x03)'}</span>
                                </div>
                            </div>
                            <div class="btn-group">
                                ${p.status === 'Active' ? `<button class="btn btn-secondary" onclick="testConnection('${p.provider}')">Test Connectivity</button>` : ''}
                                ${disconnectBtn}
                                ${p.status !== 'Active' ? connectBtn : ''}
                            </div>
                        </div>`;
                    }

                    async function loadAuditLogs() {
                        if (!uiCapabilities.canViewAuditLogs) return;
                        const tbody = document.getElementById('auditTableBody');
                        try {
                            const res = await fetch('/api/v1/connections/audit-logs');
                            if (!res.ok) return;
                            const logs = await res.json();
                            if (logs.length === 0) {
                                tbody.innerHTML = '<tr><td colspan="4" style="text-align:center; color: var(--text-muted);">No audit records found.</td></tr>';
                                return;
                            }
                            tbody.innerHTML = logs.map(l => `
                                <tr>
                                    <td>${new Date(l.timestampUtc).toLocaleString()}</td>
                                    <td><strong>${l.provider.toUpperCase()}</strong></td>
                                    <td>${l.eventType}</td>
                                    <td>${l.details}</td>
                                </tr>
                            `).join('');
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
                        if (!key) return alert('Please provide an API key.');

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
                                showAlert(`Successfully connected ${currentProviderForModal.toUpperCase()}! Credentials encrypted.`, 'success');
                                loadConnections();
                            } else {
                                const err = await res.text();
                                alert('Error: ' + err);
                            }
                        } catch (err) {
                            alert('Failed to connect: ' + err.message);
                        }
                    }

                    async function revokeConnection(provider) {
                        if (!confirm(`Are you sure you want to disconnect ${provider.toUpperCase()}?`)) return;
                        try {
                            const res = await fetch(`/api/v1/connections/${provider}/revoke`, {
                                method: 'DELETE',
                                headers: { 'Content-Type': 'application/json' },
                                body: JSON.stringify({ reason: 'User clicked disconnect' })
                            });

                            if (res.ok) {
                                showAlert(`Connection for ${provider.toUpperCase()} has been revoked.`, 'success');
                                loadConnections();
                            } else {
                                alert('Backend rejected revocation: Forbidden.');
                            }
                        } catch (err) {
                            alert('Error: ' + err.message);
                        }
                    }

                    async function testConnection(provider) {
                        showAlert(`Testing connectivity with ${provider.toUpperCase()}...`, 'success');
                        try {
                            const res = await fetch(`/api/v1/connections/${provider}/test`, { method: 'POST' });
                            const data = await res.json();
                            showAlert(data.success ? `Connectivity OK: ${data.message}` : `Failed: ${data.message}`, data.success ? 'success' : 'danger');
                        } catch (err) {
                            showAlert(`Connectivity Error: ${err.message}`, 'danger');
                        }
                    }

                    // Security Anti-Tamper Simulation
                    async function simulateTamperAttempt(action, resourceId, targetTenantId) {
                        showAlert(`[ATTACK SIMULATION] Attempting unauthorized backend action '${action}'...`, 'danger');
                        try {
                            const res = await fetch('/api/v1/ui/simulate-tamper', {
                                method: 'POST',
                                headers: { 'Content-Type': 'application/json' },
                                body: JSON.stringify({
                                    targetAction: action,
                                    targetResourceId: resourceId,
                                    targetTenantId: targetTenantId
                                })
                            });

                            const data = await res.json();
                            if (res.status === 403) {
                                showAlert(`🛡️ [BACKEND BLOCKED 403 FORBIDDEN] ${data.error} (Reason: ${data.reason || 'Cross-tenant violation'}) - Violation logged to security audit trail!`, 'danger');
                            } else if (res.ok) {
                                showAlert(`Action executed: ${data.message}`, 'success');
                            } else {
                                showAlert(`Response (${res.status}): ${JSON.stringify(data)}`, 'danger');
                            }
                        } catch (err) {
                            showAlert(`Request Failed: ${err.message}`, 'danger');
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
