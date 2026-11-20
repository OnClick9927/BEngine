'use strict';

const vscode = require('vscode');
const path = require('node:path');
const protocol = require('./protocol');
const { samePath, verifyTarget } = require('./processes');

/** @param {import('vscode').ExtensionContext} context */
function activate(context) {
  const log = vscode.window.createOutputChannel('BEngine');
  const status = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 30);
  status.text = '$(debug-disconnect) BEngine';
  status.command = 'bengine.attach';
  status.tooltip = 'Attach to BEngine Editor / 附加到编辑器';
  let refreshTimer;
  let attaching = false;
  const write = message => log.appendLine(`[${new Date().toISOString()}] ${message}`);

  async function roots() {
    const locations = (vscode.workspace.workspaceFolders || []).filter(folder => folder.uri.scheme === 'file').map(folder => folder.uri.fsPath);
    const active = vscode.window.activeTextEditor?.document.uri;
    if (active?.scheme === 'file') locations.unshift(path.dirname(active.fsPath));
    const result = [];
    for (const location of locations) {
      const root = await protocol.projectRoot(location);
      if (root && !result.some(item => samePath(item, root))) result.push(root);
    }
    return result;
  }

  async function selectProject() {
    requireTrusted();
    const projects = await roots();
    if (!projects.length) throw new Error('Open the BEngine project folder containing Project.yaml. / 请打开包含 Project.yaml 的工程文件夹。');
    if (projects.length === 1) return projects[0];
    const selected = await vscode.window.showQuickPick(projects.map(root => ({ label: path.basename(root), description: root, root })),
      { title: 'BEngine: Select project / 选择工程', matchOnDescription: true });
    return selected?.root;
  }

  function requireTrusted() {
    if (!vscode.workspace.isTrusted) throw new Error('Trust this workspace before using BEngine integration. / 请先信任此工作区。');
    if (vscode.env.remoteName) throw new Error('Attach from a local VS Code window running on the same computer as BEngine Editor.');
  }

  async function selectSolution(root, solutionPath) {
    const folder = vscode.workspace.getWorkspaceFolder(vscode.Uri.file(root));
    if (!folder) return;
    const value = vscode.workspace.workspaceFile ? solutionPath : path.relative(folder.uri.fsPath, solutionPath).replace(/\\/g, '/');
    // Microsoft C# declares this setting at window scope, including multi-root workspaces.
    // Change only the selected solution in workspace settings, never user preferences.
    const config = vscode.workspace.getConfiguration('dotnet');
    if (config.get('defaultSolution') !== value) {
      await config.update('defaultSolution', value, vscode.ConfigurationTarget.Workspace);
    }
  }

  async function synchronize() {
    const root = await selectProject();
    if (!root) return;
    const targets = await protocol.readTargets(root, undefined, write);
    if (!targets.length) throw new Error('Open this project in BEngine Editor with the IDE Integration package enabled.');
    const timeout = vscode.workspace.getConfiguration('bengine', vscode.Uri.file(root)).get('syncTimeoutSeconds', 45);
    const solutionPath = await vscode.window.withProgress({ location: vscode.ProgressLocation.Notification,
      title: 'BEngine: Synchronizing C# project / 正在同步 C# 工程', cancellable: true },
    (_, token) => protocol.synchronize(root, { timeoutMs: timeout * 1000, cancelled: () => token.isCancellationRequested }));
    await selectSolution(root, solutionPath);
    write(`Synchronized ${solutionPath}`);
    vscode.window.setStatusBarMessage('BEngine: C# project synchronized / C# 工程已同步', 4000);
    return solutionPath;
  }

  async function attach() {
    if (attaching) return;
    attaching = true;
    try {
      requireTrusted();
      const projects = await roots();
      if (!projects.length) throw new Error('Open the BEngine project folder containing Project.yaml. / 请打开包含 Project.yaml 的工程文件夹。');
      const all = (await Promise.all(projects.map(root => protocol.readTargets(root, undefined, write)))).flat();
      if (!all.length) throw new Error('No running BEngine Editor for this workspace. Open the project in BEngine and enable IDE Integration.');
      const current = projects[0];
      all.sort((a, b) => Number(samePath(b.projectPath, current)) - Number(samePath(a.projectPath, current)) || a.processId - b.processId);
      let target = all[0];
      if (all.length > 1) {
        const selected = await vscode.window.showQuickPick(all.map(value => ({ label: value.projectName,
          description: `${value.displayName || 'Editor'} · PID ${value.processId}`,
          detail: value.projectPath, value })), { title: 'BEngine: Attach to Editor / 附加到编辑器', matchOnDescription: true, matchOnDetail: true });
        if (!selected) return;
        target = selected.value;
      }
      const existing = vscode.debug.activeDebugSession;
      if (existing?.type === 'coreclr' && String(existing.configuration.processId) === String(target.processId)) {
        vscode.window.setStatusBarMessage('BEngine: Already attached / 已附加', 3000);
        return true;
      }
      const csharp = vscode.extensions.getExtension('ms-dotnettools.csharp');
      if (!csharp) throw new Error('Install the Microsoft C# extension (ms-dotnettools.csharp).');
      await csharp.activate();
      const project = await protocol.readProject(target.projectPath);
      await selectSolution(target.projectPath, project.solutionPath);
      // Recheck after extension activation/selection: the editor may have exited meanwhile.
      if (!await verifyTarget(target)) throw new Error('The BEngine Editor process exited or changed. Retry Attach.');
      const folder = vscode.workspace.getWorkspaceFolder(vscode.Uri.file(target.projectPath));
      const configuration = {
        name: `BEngine: ${target.projectName}`, type: 'coreclr', request: 'attach', processId: String(target.processId),
        justMyCode: vscode.workspace.getConfiguration('bengine', folder?.uri).get('justMyCode', false),
        requireExactSource: true
      };
      const success = await vscode.debug.startDebugging(folder, configuration);
      if (!success) throw new Error('The C# debugger could not attach. See the BEngine output and C# debugger log.');
      write(`Attached to ${target.projectName}, PID ${target.processId}`);
      return true;
    } finally { attaching = false; }
  }

  async function refresh() {
    const projects = await roots();
    await vscode.commands.executeCommand('setContext', 'bengine.project', projects.length > 0);
    if (!projects.length || !vscode.workspace.isTrusted) { status.hide(); return; }
    status.show();
    for (const root of projects.slice(0, 1)) {
      try { const project = await protocol.readProject(root); await selectSolution(root, project.solutionPath); }
      catch (error) { write(`Project not synchronized: ${error.message}`); }
    }
  }

  const safe = action => async () => {
    try { return await action(); }
    catch (error) {
      if (error.code === 'CANCELLED') return;
      write(error.stack || String(error));
      vscode.window.showErrorMessage(`BEngine: ${error.message}`, 'Show Log / 查看日志')
        .then(choice => { if (choice) log.show(true); });
      return false;
    }
  };
  const scheduleRefresh = () => { clearTimeout(refreshTimer); refreshTimer = setTimeout(() => safe(refresh)(), 250); };
  const watcher = vscode.workspace.createFileSystemWatcher('**/Library/IDE/project.json');
  context.subscriptions.push(log, status, watcher,
    { dispose() { clearTimeout(refreshTimer); } },
    vscode.commands.registerCommand('bengine.attach', safe(attach)),
    vscode.commands.registerCommand('bengine.syncProject', safe(synchronize)),
    vscode.commands.registerCommand('bengine.openSolution', safe(async () => {
      const root = await selectProject(); if (!root) return;
      const project = await protocol.readProject(root);
      await selectSolution(root, project.solutionPath);
      await vscode.commands.executeCommand('vscode.open', vscode.Uri.file(project.solutionPath));
    })),
    vscode.commands.registerCommand('bengine.showLog', () => log.show(true)),
    watcher.onDidChange(scheduleRefresh), watcher.onDidCreate(scheduleRefresh), watcher.onDidDelete(scheduleRefresh),
    vscode.workspace.onDidChangeWorkspaceFolders(scheduleRefresh), vscode.window.onDidChangeActiveTextEditor(scheduleRefresh),
    vscode.workspace.onDidGrantWorkspaceTrust(scheduleRefresh));
  safe(refresh)();
  return { refresh, attach, synchronize };
}

module.exports = { activate };
