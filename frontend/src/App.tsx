
import { useEffect, useState } from 'react';
import type { FormEvent } from 'react';

const API = 'http://localhost:5000/api';

type User = {
  id: string;
  name: string;
  email: string;
  role: string;
};

type Doc = {
  id: string;
  name: string;
  category: string;
  createdAt: string;
  ownerId: string;
  ownerName?: string;
};

type AuditLog = {
  id: string;
  userId: string;
  documentId: string;
  action: string;
  detail: string;
  createdAt: string;
};

async function apiRequest(
  path: string,
  token = '',
  init: RequestInit = {}
): Promise<any> {
  const response = await fetch(`${API}${path}`, {
    ...init,
    headers: {
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...(init.headers || {})
    }
  });

  if (!response.ok) {
    const text = await response.text();
    throw new Error(`${path} failed: ${response.status} ${text}`);
  }

  if (response.status === 204) return null;

  const text = await response.text();
  return text ? JSON.parse(text) : null;
}

export default function App() {
  const [token, setToken] = useState(
    localStorage.getItem('token') || ''
  );

  const [user, setUser] = useState<User | null>(null);
  const [docs, setDocs] = useState<Doc[]>([]);
  const [message, setMessage] = useState('');
  const [loading, setLoading] = useState(false);

  const [name, setName] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [isRegistering, setIsRegistering] = useState(false);

  const [file, setFile] = useState<File | null>(null);
  const [category, setCategory] = useState('General');

  // TXT EDITOR STATES
  const [editingDoc, setEditingDoc] = useState<Doc | null>(null);
  const [editContent, setEditContent] = useState('');
  const [savingEdit, setSavingEdit] = useState(false);

  async function load() {
    try {
      const currentUser = await apiRequest('/me', token);
      setUser(currentUser);

      const documents = await apiRequest('/documents', token);
      setDocs(documents || []);

      setMessage('');
    } catch (error) {
      setMessage(String(error));
    }
  }

  useEffect(() => {
    if (token) {
      load();
    }
  }, [token]);

  async function login(e: FormEvent) {
    e.preventDefault();
    setLoading(true);
    setMessage('');

    try {
      const data = await apiRequest('/auth/login', '', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json'
        },
        body: JSON.stringify({ email, password })
      });

      localStorage.setItem('token', data.token);
      setToken(data.token);
      setPassword('');
      setMessage('Login successful!');
    } catch (error) {
      setMessage(String(error));
    } finally {
      setLoading(false);
    }
  }

  async function register(e: FormEvent) {
    e.preventDefault();
    setLoading(true);
    setMessage('');

    try {
      await apiRequest('/auth/register', '', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json'
        },
        body: JSON.stringify({
          name,
          email,
          password
        })
      });

      setMessage('Registration successful! Please sign in.');

      setIsRegistering(false);
      setName('');
      setPassword('');
    } catch (error) {
      setMessage(String(error));
    } finally {
      setLoading(false);
    }
  }

  function logout() {
    localStorage.removeItem('token');
    setToken('');
    setUser(null);
    setDocs([]);
    setEditingDoc(null);
    setEditContent('');
    setMessage('');
  }

  async function upload(e: FormEvent) {
    e.preventDefault();

    if (!file) {
      setMessage('Please select a document.');
      return;
    }

    if (file.size > 10 * 1024 * 1024) {
      setMessage('File size must be below 10 MB.');
      return;
    }

    const formData = new FormData();
    formData.append('file', file);
    formData.append('category', category);

    try {
      await apiRequest(
        `/documents?category=${encodeURIComponent(category)}`,
        token,
        {
          method: 'POST',
          body: formData
        }
      );

      setFile(null);
      setMessage('Document uploaded successfully!');
      await load();
    } catch (error) {
      setMessage(String(error));
    }
  }

  async function download(id: string, documentName: string) {
    try {
      const response = await fetch(
        `${API}/documents/${id}/download`,
        {
          headers: {
            Authorization: `Bearer ${token}`
          }
        }
      );

      if (!response.ok) {
        throw new Error(`Download failed: ${response.status}`);
      }

      const blob = await response.blob();
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');

      link.href = url;
      link.download = documentName;

      document.body.appendChild(link);
      link.click();
      link.remove();

      URL.revokeObjectURL(url);

      setMessage('Download successful!');
    } catch (error) {
      setMessage(String(error));
    }
  }

  async function remove(id: string) {
    if (!window.confirm(
      'Are you sure you want to delete this document?'
    )) {
      return;
    }

    try {
      await apiRequest(`/documents/${id}`, token, {
        method: 'DELETE'
      });

      setMessage('Document deleted successfully!');
      await load();
    } catch (error) {
      setMessage(String(error));
    }
  }

  // OPEN TXT EDITOR
  async function openEditor(document: Doc) {
    try {
      const data = await apiRequest(
        `/documents/${document.id}/content`,
        token
      );

      setEditingDoc(document);
      setEditContent(data.content || '');
      setMessage('Document opened for editing.');
    } catch (error) {
      setMessage(String(error));
    }
  }

  // SAVE TXT CONTENT
  async function saveEditedContent() {
    if (!editingDoc) return;

    setSavingEdit(true);
    setMessage('');

    try {
      await apiRequest(
        `/documents/${editingDoc.id}/content`,
        token,
        {
          method: 'PUT',
          headers: {
            'Content-Type': 'application/json'
          },
          body: JSON.stringify({
            content: editContent
          })
        }
      );

      setMessage('Document content saved successfully!');
      setEditingDoc(null);
      setEditContent('');

      await load();
    } catch (error) {
      setMessage(String(error));
    } finally {
      setSavingEdit(false);
    }
  }

  // LOGIN / REGISTER PAGE
  if (!token) {
    return (
      <main className="auth">
        <form
          className="panel"
          onSubmit={isRegistering ? register : login}
        >
          <div className="brand">🔐 TrustVault</div>

          <h1>Secure Document Sharing</h1>

          <p>
            {isRegistering
              ? 'Create your TrustVault account.'
              : 'Sign in to access your workspace.'}
          </p>

          {isRegistering && (
            <label>
              Name

              <input
                value={name}
                onChange={e => setName(e.target.value)}
                required
              />
            </label>
          )}

          <label>
            Email

            <input
              type="email"
              value={email}
              onChange={e => setEmail(e.target.value)}
              required
            />
          </label>

          <label>
            Password

            <input
              type="password"
              value={password}
              onChange={e => setPassword(e.target.value)}
              minLength={10}
              required
            />
          </label>

          <button type="submit" disabled={loading}>
            {loading
              ? 'Please wait...'
              : isRegistering
                ? 'Sign Up'
                : 'Sign In'}
          </button>

          <button
            type="button"
            className="secondary"
            onClick={() => {
              setIsRegistering(!isRegistering);
              setMessage('');
            }}
          >
            {isRegistering ? 'Sign In' : 'Create Account'}
          </button>

          {message && (
            <p className="notice">{message}</p>
          )}
        </form>
      </main>
    );
  }

  // MAIN DASHBOARD
  return (
    <main className="page">
      <header>
        <div className="brand">🔐 TrustVault</div>

        <div>
          {user?.name} · {user?.role}

          <button
            className="secondary"
            onClick={logout}
          >
            Log Out
          </button>
        </div>
      </header>

      <section className="intro">
        <div>
          <h1>Your Secure Workspace</h1>

          <p>
            Documents are visible only to their owners
            and authorized users.
          </p>
        </div>

        <span className="pill">
          Protected Access
        </span>
      </section>

      {message && (
        <p className="notice">{message}</p>
      )}

      {/* OWNER / ADMIN UPLOAD */}
      {(user?.role === 'Owner' ||
        user?.role === 'Admin') && (
        <form
          className="panel upload"
          onSubmit={upload}
        >
          <h2>Upload a Document</h2>

          <input
            type="file"
            onChange={e =>
              setFile(e.target.files?.[0] || null)
            }
            required
          />

          <input
            value={category}
            onChange={e => setCategory(e.target.value)}
            placeholder="Document category"
          />

          <button type="submit">
            Upload Document
          </button>

          <small>
            Maximum 10 MB. Allowed: PDF, TXT, PNG,
            JPG, DOCX, XLSX.
          </small>
        </form>
      )}

      {/* DOCUMENT LIST */}
      <section className="panel">
        <div className="sectionhead">
          <h2>My Accessible Documents</h2>

          <button
            className="secondary"
            onClick={load}
          >
            Refresh
          </button>
        </div>

        {docs.length === 0 ? (
          <p>No documents available yet.</p>
        ) : (
          <div className="table">
            {docs.map(document => (
              <div className="doc" key={document.id}>
                <div>
                  <strong>{document.name}</strong>

                  <small>
                    {document.category} ·{' '}
                    {new Date(
                      document.createdAt
                    ).toLocaleString()}
                  </small>
                </div>

                <div className="actions">

                  {/* EDIT BUTTON */}
                  {document.name.toLowerCase().endsWith('.txt') && (
                    <button
                      onClick={() => openEditor(document)}
                    >
                      Edit
                    </button>
                  )}

                  <button
                    onClick={() =>
                      download(document.id, document.name)
                    }
                  >
                    Download
                  </button>

                  {(user?.role === 'Admin' ||
                    document.ownerId === user?.id) && (
                    <button
                      className="danger"
                      onClick={() =>
                        remove(document.id)
                      }
                    >
                      Delete
                    </button>
                  )}
                </div>
              </div>
            ))}
          </div>
        )}
      </section>

      {/* TXT EDITOR */}
      {editingDoc && (
        <section className="panel">
          <h2>Document Editor</h2>

          <p>
            Editing: <strong>{editingDoc.name}</strong>
          </p>

          <textarea
            value={editContent}
            onChange={e => setEditContent(e.target.value)}
            rows={15}
            style={{
              width: '100%',
              minHeight: '300px',
              padding: '12px',
              fontSize: '15px',
              fontFamily: 'monospace',
              resize: 'vertical'
            }}
          />

          <div className="actions">
            <button
              onClick={saveEditedContent}
              disabled={savingEdit}
            >
              {savingEdit ? 'Saving...' : 'Save Changes'}
            </button>

            <button
              className="secondary"
              onClick={() => {
                setEditingDoc(null);
                setEditContent('');
              }}
            >
              Cancel
            </button>
          </div>
        </section>
      )}

      {/* OWNER PERMISSION PANEL */}
      {user?.role === 'Owner' && (
        <OwnerPermissionPanel
          token={token}
          docs={docs.filter(d => d.ownerId === user.id)}
          showMessage={setMessage}
        />
      )}

      {/* ADMIN PANEL */}
      {user?.role === 'Admin' && (
        <AdminPanel
          token={token}
          docs={docs}
          refreshDocuments={load}
          showMessage={setMessage}
        />
      )}
    </main>
  );
}

// OWNER PERMISSION PANEL
function OwnerPermissionPanel({
  token,
  docs,
  showMessage
}: {
  token: string;
  docs: Doc[];
  showMessage: (message: string) => void;
}) {
  const [users, setUsers] = useState<User[]>([]);
  const [selectedDocument, setSelectedDocument] = useState('');
  const [selectedUser, setSelectedUser] = useState('');
  const [canRead, setCanRead] = useState(true);
  const [canEdit, setCanEdit] = useState(false);
  const [error, setError] = useState('');

  async function loadUsers() {
    try {
      const data = await apiRequest('/users/shareable', token);
      setUsers(data || []);
      setError('');
    } catch (err) {
      setError(String(err));
    }
  }

  useEffect(() => {
    loadUsers();
  }, [token]);

  async function grantPermission(e: FormEvent) {
    e.preventDefault();

    if (!selectedDocument || !selectedUser) {
      showMessage('Please select document and user.');
      return;
    }

    try {
      await apiRequest(
        `/documents/${selectedDocument}/permissions`,
        token,
        {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json'
          },
          body: JSON.stringify({
            userId: selectedUser,
            canRead,
            canEdit: canRead && canEdit
          })
        }
      );

      showMessage('Document permission updated successfully!');
    } catch (err) {
      showMessage(String(err));
    }
  }

  async function revokePermission(e: FormEvent) {
    e.preventDefault();

    if (!selectedDocument || !selectedUser) {
      showMessage('Please select document and user.');
      return;
    }

    if (!window.confirm('Remove this user document access?')) {
      return;
    }

    try {
      await apiRequest(
        `/documents/${selectedDocument}/permissions/${selectedUser}`,
        token,
        {
          method: 'DELETE'
        }
      );

      showMessage('Document access revoked successfully!');
    } catch (err) {
      showMessage(String(err));
    }
  }

  return (
    <section className="panel">
      <h2>Share Document / Permissions</h2>

      <p>
        Share your documents with Editor or Viewer accounts.
      </p>

      {error && (
        <p className="notice">{error}</p>
      )}

      <form className="upload" onSubmit={grantPermission}>
        <label>
          Select Your Document

          <select
            value={selectedDocument}
            onChange={e => setSelectedDocument(e.target.value)}
            required
          >
            <option value="">Choose document</option>

            {docs.map(document => (
              <option key={document.id} value={document.id}>
                {document.name}
              </option>
            ))}
          </select>
        </label>

        <label>
          Select Editor / Viewer

          <select
            value={selectedUser}
            onChange={e => setSelectedUser(e.target.value)}
            required
          >
            <option value="">Choose user</option>

            {users.map(item => (
              <option key={item.id} value={item.id}>
                {item.name} ({item.role})
              </option>
            ))}
          </select>
        </label>

        <label>
          <input
            type="checkbox"
            checked={canRead}
            onChange={e => setCanRead(e.target.checked)}
          />
          Allow View / Download
        </label>

        <label>
          <input
            type="checkbox"
            checked={canEdit}
            disabled={!canRead}
            onChange={e => setCanEdit(e.target.checked)}
          />
          Allow Edit
        </label>

        <button type="submit">
          Grant / Update Permission
        </button>
      </form>

      <form onSubmit={revokePermission}>
        <button type="submit" className="danger">
          Revoke Access
        </button>
      </form>
    </section>
  );
}

// ADMIN PANEL
function AdminPanel({
  token,
  docs,
  refreshDocuments,
  showMessage
}: {
  token: string;
  docs: Doc[];
  refreshDocuments: () => Promise<void>;
  showMessage: (message: string) => void;
}) {
  const [users, setUsers] = useState<User[]>([]);
  const [logs, setLogs] = useState<AuditLog[]>([]);
  const [error, setError] = useState('');

  const [newName, setNewName] = useState('');
  const [newEmail, setNewEmail] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [newRole, setNewRole] = useState('Viewer');

  const [selectedDocument, setSelectedDocument] = useState('');
  const [selectedUser, setSelectedUser] = useState('');
  const [canRead, setCanRead] = useState(true);
  const [canEdit, setCanEdit] = useState(false);

  async function loadAdminData() {
    try {
      const userData = await apiRequest('/users', token);
      const auditData = await apiRequest('/audit', token);

      setUsers(userData || []);
      setLogs(auditData || []);
      setError('');
    } catch (err) {
      setError(String(err));
    }
  }

  useEffect(() => {
    loadAdminData();
  }, [token]);

  async function createUser(e: FormEvent) {
    e.preventDefault();

    try {
      await apiRequest('/users', token, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json'
        },
        body: JSON.stringify({
          name: newName,
          email: newEmail,
          password: newPassword,
          role: newRole
        })
      });

      showMessage('New user created successfully!');

      setNewName('');
      setNewEmail('');
      setNewPassword('');
      setNewRole('Viewer');

      await loadAdminData();
    } catch (err) {
      showMessage(String(err));
    }
  }

  async function grantPermission(e: FormEvent) {
    e.preventDefault();

    if (!selectedDocument || !selectedUser) {
      showMessage('Select a document and a user.');
      return;
    }

    try {
      await apiRequest(
        `/documents/${selectedDocument}/permissions`,
        token,
        {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json'
          },
          body: JSON.stringify({
            userId: selectedUser,
            canRead,
            canEdit: canRead && canEdit
          })
        }
      );

      showMessage('Document permission updated successfully!');
    } catch (err) {
      showMessage(String(err));
    }
  }

  async function revokePermission(e: FormEvent) {
    e.preventDefault();

    if (!selectedDocument || !selectedUser) {
      showMessage('Select a document and a user.');
      return;
    }

    if (!window.confirm('Remove this user document access?')) {
      return;
    }

    try {
      await apiRequest(
        `/documents/${selectedDocument}/permissions/${selectedUser}`,
        token,
        {
          method: 'DELETE'
        }
      );

      showMessage('Document access revoked successfully!');
    } catch (err) {
      showMessage(String(err));
    }
  }

  return (
    <section className="panel">
      <h2>Administrator Dashboard</h2>

      {error && (
        <p className="notice">{error}</p>
      )}

      <hr />

      <h3>Create New User</h3>

      <form className="upload" onSubmit={createUser}>
        <label>
          Full Name

          <input
            value={newName}
            onChange={e => setNewName(e.target.value)}
            placeholder="Enter user name"
            required
          />
        </label>

        <label>
          Email

          <input
            type="email"
            value={newEmail}
            onChange={e => setNewEmail(e.target.value)}
            placeholder="Enter email"
            required
          />
        </label>

        <label>
          Password

          <input
            type="password"
            value={newPassword}
            onChange={e => setNewPassword(e.target.value)}
            placeholder="Minimum 10 characters"
            minLength={10}
            required
          />
        </label>

        <label>
          Select User Role

          <select
            value={newRole}
            onChange={e => setNewRole(e.target.value)}
          >
            <option value="Admin">Admin</option>
            <option value="Owner">Owner</option>
            <option value="Editor">Editor</option>
            <option value="Viewer">Viewer</option>
          </select>
        </label>

        <button type="submit">
          Create User
        </button>
      </form>

      <hr />

      <h3>All Registered Users</h3>

      <button
        className="secondary"
        onClick={loadAdminData}
      >
        Refresh Users
      </button>

      {users.length === 0 ? (
        <p>No users found.</p>
      ) : (
        users.map(item => (
          <div className="doc" key={item.id}>
            <div>
              <strong>{item.name}</strong>
              <small>{item.email}</small>
            </div>

            <span className="pill">
              {item.role}
            </span>
          </div>
        ))
      )}

      <hr />

      <h3>Document Permission Management</h3>

      <form className="upload" onSubmit={grantPermission}>
        <label>
          Select Document

          <select
            value={selectedDocument}
            onChange={e => setSelectedDocument(e.target.value)}
            required
          >
            <option value="">Choose a document</option>

            {docs.map(document => (
              <option key={document.id} value={document.id}>
                {document.name}
              </option>
            ))}
          </select>
        </label>

        <label>
          Select User

          <select
            value={selectedUser}
            onChange={e => setSelectedUser(e.target.value)}
            required
          >
            <option value="">Choose a user</option>

            {users
              .filter(item => item.role !== 'Admin')
              .map(item => (
                <option key={item.id} value={item.id}>
                  {item.name} ({item.role})
                </option>
              ))}
          </select>
        </label>

        <label>
          <input
            type="checkbox"
            checked={canRead}
            onChange={e => setCanRead(e.target.checked)}
          />
          Allow View / Download
        </label>

        <label>
          <input
            type="checkbox"
            checked={canEdit}
            disabled={!canRead}
            onChange={e => setCanEdit(e.target.checked)}
          />
          Allow Edit
        </label>

        <button type="submit">
          Grant / Update Access
        </button>
      </form>

      <form onSubmit={revokePermission}>
        <button className="danger" type="submit">
          Revoke Selected User Access
        </button>
      </form>

      <button
        className="secondary"
        onClick={refreshDocuments}
      >
        Refresh Documents
      </button>

      <hr />

      <h3>All Documents</h3>

      {docs.map(document => (
        <div className="doc" key={document.id}>
          <div>
            <strong>{document.name}</strong>

            <small>
              {document.category} · Owner:{' '}
              {document.ownerName || document.ownerId}
            </small>
          </div>
        </div>
      ))}

      <hr />

      <h3>Recent Audit Activity</h3>

      {logs.length === 0 ? (
        <p>No audit activity found.</p>
      ) : (
        logs.map(log => (
          <p key={log.id}>
            <small>
              {new Date(log.createdAt).toLocaleString()}
              {' · '}
              {log.action}
              {' · '}
              {log.detail}
            </small>
          </p>
        ))
      )}
    </section>
  );
}
