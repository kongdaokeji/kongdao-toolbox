import { reactive } from 'vue'
import { signalingService } from '../services/signaling'
import { webRtcService } from '../services/webrtc'
import type { GroupDevice, FileTransferTask, TransferGroup, SignalingMessage, PeerInfo } from '../types'

const CHUNK_SIZE = 16384

function formatFileSize(bytes: number): string {
  if (bytes >= 1 << 30) return (bytes / (1 << 30)).toFixed(2) + ' GB'
  if (bytes >= 1 << 20) return (bytes / (1 << 20)).toFixed(1) + ' MB'
  if (bytes >= 1 << 10) return (bytes / (1 << 10)).toFixed(0) + ' KB'
  return bytes + ' B'
}

function formatSpeed(mbps: number): string {
  if (mbps >= 1000) return (mbps / 1000).toFixed(2) + ' GB/s'
  if (mbps >= 1) return mbps.toFixed(1) + ' MB/s'
  if (mbps > 0) return (mbps * 1024).toFixed(0) + ' KB/s'
  return ''
}

const state = reactive({
  connected: false,
  group: null as TransferGroup | null,
  devices: [] as GroupDevice[],
  transfers: [] as FileTransferTask[],
  toasts: [] as { id: number; msg: string; color: string }[],
  signalingUrl: 'wss://transfer.tubawinui3.cn',
  deviceName: 'Web Browser',
})

let toastId = 0

function showToast(msg: string, color = 'info') {
  const id = ++toastId
  state.toasts.push({ id, msg, color })
  setTimeout(() => {
    state.toasts = state.toasts.filter(t => t.id !== id)
  }, 4000)
}

function findDevice(deviceId: string): GroupDevice | undefined {
  return state.devices.find(d => d.deviceId === deviceId)
}

function upsertDevice(peer: PeerInfo, connectionType: GroupDevice['connectionType'] = 'webrtc-p2p') {
  const existing = findDevice(peer.deviceId)
  if (existing) {
    existing.isOnline = true
    existing.deviceName = peer.deviceName
    if (peer.lanIp) existing.lanIp = peer.lanIp
    existing.connectionType = connectionType
    return
  }
  if (peer.deviceId === signalingService.deviceId) return
  state.devices.push({
    deviceId: peer.deviceId,
    deviceName: peer.deviceName,
    lanIp: peer.lanIp,
    isOnline: true,
    connectionType,
    joinedAt: peer.joinedAt,
  })
}

function handleSignalingMessage(msg: SignalingMessage) {
  switch (msg.type) {
    case 'joined': {
      if (msg.groupCode && state.group) {
        state.group.groupId = msg.groupCode
        state.group.groupName = msg.groupName ?? state.group.groupName
      }
      if (msg.peers) {
        for (const p of msg.peers) {
          upsertDevice(p)
        }
      }
      break
    }
    case 'device-joined': {
      if (msg.device) {
        upsertDevice(msg.device, msg.device.lanIp ? 'lan' : 'webrtc-p2p')
      }
      break
    }
    case 'device-left': {
      if (msg.deviceId) {
        const dev = findDevice(msg.deviceId)
        if (dev) dev.isOnline = false
      }
      break
    }
    case 'sdp-offer': {
      if (msg.from && msg.sdp) {
        webRtcService.handleOffer(msg.from, msg.sdp).then(answer => {
          signalingService.sendSdpAnswer(msg.from!, answer)
        }).catch(err => {
          showToast(`SDP 处理失败: ${err.message}`, 'error')
        })
      }
      break
    }
    case 'sdp-answer': {
      if (msg.from && msg.sdp) {
        webRtcService.handleAnswer(msg.from, msg.sdp)
      }
      break
    }
    case 'ice-candidate': {
      if (msg.from && msg.candidate) {
        webRtcService.addIceCandidate(msg.from, msg.candidate, msg.sdpMid, msg.sdpMlineIndex)
      }
      break
    }
    case 'file-offer': {
      if (msg.from && msg.fileId) {
        const fromDev = findDevice(msg.from)
        const task: FileTransferTask = {
          fileId: msg.fileId,
          fileName: msg.fileName ?? '未知文件',
          fileSize: msg.fileSize ?? 0,
          sha256: msg.sha256 ?? '',
          chunkSize: msg.chunkSize ?? CHUNK_SIZE,
          totalChunks: msg.totalChunks ?? 0,
          completedChunks: 0,
          direction: 'receiving',
          status: 'pending',
          connectionType: fromDev?.connectionType ?? 'webrtc-p2p',
          fromDeviceId: msg.from,
          fromDeviceName: fromDev?.deviceName ?? '未知设备',
          toDeviceId: signalingService.deviceId,
          speedMbps: 0,
          bytesTransferred: 0,
          startTime: Date.now(),
          errorMessage: '',
        }
        state.transfers.push(task)
        showToast(`收到文件: ${task.fileName} (${formatFileSize(task.fileSize)})`, 'info')
        signalingService.sendFileAccept(msg.from, msg.fileId)
        task.status = 'transferring'
      }
      break
    }
    case 'file-reject': {
      if (msg.fileId) {
        const task = state.transfers.find(t => t.fileId === msg.fileId)
        if (task) {
          task.status = 'failed'
          task.errorMessage = '接收方拒绝'
        }
      }
      break
    }
  }
}

function initServices() {
  signalingService.onMessage = handleSignalingMessage
  signalingService.onConnected = () => {
    state.connected = true
    showToast('已连接到信令服务器', 'success')
  }
  signalingService.onDisconnected = () => {
    state.connected = false
    showToast('已断开连接', 'warning')
  }
  signalingService.onError = (msg) => {
    showToast(msg, 'error')
  }

  webRtcService.onPeerConnected = (deviceId) => {
    const dev = findDevice(deviceId)
    if (dev) dev.connectionType = 'webrtc-p2p'
  }
  webRtcService.onPeerDisconnected = (deviceId) => {
    const dev = findDevice(deviceId)
    if (dev) dev.isOnline = false
  }
  webRtcService.onFileOfferReceived = (task) => {
    state.transfers.push(task)
  }
  webRtcService.onTransferProgressChanged = (task) => {
    const existing = state.transfers.find(t => t.fileId === task.fileId)
    if (existing) {
      Object.assign(existing, task)
    }
  }
  webRtcService.onTransferCompleted = (task) => {
    const existing = state.transfers.find(t => t.fileId === task.fileId)
    if (existing) {
      Object.assign(existing, task)
    }
    showToast(`${task.fileName} 接收完成`, 'success')
  }
  webRtcService.onTransferFailed = (task) => {
    const existing = state.transfers.find(t => t.fileId === task.fileId)
    if (existing) {
      Object.assign(existing, task)
    }
    showToast(`${task.fileName} 传输失败: ${task.errorMessage}`, 'error')
  }
}

async function createGroup(groupName: string, password?: string) {
  try {
    signalingService.signalingUrl = state.signalingUrl
    const code = await signalingService.createGroup(groupName, password)
    if (!code) {
      showToast('创建群组失败: 服务器未返回群组码', 'error')
      return
    }
    state.group = {
      groupId: code,
      groupName,
      password: password ?? '',
      creatorDeviceId: signalingService.deviceId,
      createdAt: Date.now(),
      devices: [],
    }
    signalingService.connect(code, state.deviceName, password)
    showToast(`群组已创建，群组码: ${code}`, 'success')
  } catch (err: any) {
    showToast(`创建群组失败: ${err.message}`, 'error')
  }
}

async function joinGroup(code: string, password?: string) {
  try {
    signalingService.signalingUrl = state.signalingUrl
    const exists = await signalingService.checkGroup(code)
    if (!exists) {
      showToast('群组不存在', 'error')
      return
    }
    state.group = {
      groupId: code.toUpperCase(),
      groupName: `群组 ${code.toUpperCase()}`,
      password: password ?? '',
      creatorDeviceId: '',
      createdAt: Date.now(),
      devices: [],
    }
    signalingService.connect(code, state.deviceName, password)
    showToast(`正在加入群组 ${code}...`, 'info')
  } catch (err: any) {
    showToast(`加入群组失败: ${err.message}`, 'error')
  }
}

function leaveGroup() {
  signalingService.disconnect()
  webRtcService.closeAll()
  state.group = null
  state.devices = []
  state.transfers = []
  showToast('已离开群组', 'info')
}

async function sendFile(targetDeviceId: string, file: File) {
  if (!state.group) {
    showToast('请先加入群组', 'warning')
    return
  }

  const sha256 = await computeSha256(file)
  const totalChunks = Math.ceil(file.size / CHUNK_SIZE)
  const fileId = crypto.randomUUID().replace(/-/g, '').slice(0, 12)

  const task: FileTransferTask = {
    fileId,
    fileName: file.name,
    fileSize: file.size,
    sha256,
    chunkSize: CHUNK_SIZE,
    totalChunks,
    completedChunks: 0,
    direction: 'sending',
    status: 'pending',
    connectionType: 'webrtc-p2p',
    fromDeviceId: signalingService.deviceId,
    fromDeviceName: state.deviceName,
    toDeviceId: targetDeviceId,
    speedMbps: 0,
    bytesTransferred: 0,
    startTime: Date.now(),
    errorMessage: '',
    file,
  }

  signalingService.sendFileOffer(targetDeviceId, {
    fileId: task.fileId,
    fileName: task.fileName,
    fileSize: task.fileSize,
    chunkSize: task.chunkSize,
    totalChunks: task.totalChunks,
    sha256: task.sha256,
  })

  state.transfers.push(task)

  try {
    await webRtcService.sendFile(targetDeviceId, file, task)
    showToast(`${file.name} 发送完成`, 'success')
  } catch (err: any) {
    task.status = 'failed'
    task.errorMessage = err.message
    showToast(`发送失败: ${err.message}`, 'error')
  }
}

async function sendFileToAll(file: File) {
  const targets = state.devices.filter(d => d.isOnline && d.deviceId !== signalingService.deviceId)
  if (targets.length === 0) {
    showToast('没有在线设备', 'warning')
    return
  }
  for (const t of targets) {
    await sendFile(t.deviceId, file)
  }
}

async function computeSha256(file: File): Promise<string> {
  const buffer = await file.arrayBuffer()
  const hash = await crypto.subtle.digest('SHA-256', buffer)
  return Array.from(new Uint8Array(hash)).map(b => b.toString(16).padStart(2, '0')).join('')
}

function copyGroupCode() {
  if (state.group?.groupId) {
    navigator.clipboard.writeText(state.group.groupId)
    showToast('群组码已复制', 'success')
  }
}

initServices()

export function useFileTransfer() {
  return {
    state,
    createGroup,
    joinGroup,
    leaveGroup,
    sendFile,
    sendFileToAll,
    copyGroupCode,
    formatFileSize,
    formatSpeed,
    showToast,
  }
}
