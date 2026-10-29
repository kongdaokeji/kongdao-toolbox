import type { SignalingMessage } from '../types'

const DEFAULT_URL = 'wss://transfer.tubawinui3.cn'

class SignalingService {
  private ws: WebSocket | null = null
  private _signalingUrl = DEFAULT_URL
  private _deviceId = ''
  private _deviceName = ''
  private _groupCode = ''
  private reconnectTimer: ReturnType<typeof setTimeout> | null = null

  onMessage: ((msg: SignalingMessage) => void) | null = null
  onConnected: (() => void) | null = null
  onDisconnected: (() => void) | null = null
  onError: ((msg: string) => void) | null = null

  get isConnected() {
    return this.ws?.readyState === WebSocket.OPEN
  }

  get deviceId() {
    return this._deviceId
  }

  get groupCode() {
    return this._groupCode
  }

  set signalingUrl(url: string) {
    this._signalingUrl = url || DEFAULT_URL
  }

  generateDeviceId(): string {
    const arr = new Uint8Array(6)
    crypto.getRandomValues(arr)
    return Array.from(arr, b => b.toString(16).padStart(2, '0')).join('')
  }

  async createGroup(groupName: string, password?: string): Promise<string | null> {
    const apiUrl = this._signalingUrl.replace('wss://', 'https://').replace('ws://', 'http://') + '/api/group'
    const resp = await fetch(apiUrl, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        groupName,
        password: password || undefined,
        creatorDeviceId: this._deviceId,
      }),
    })
    if (!resp.ok) throw new Error(`创建群组失败: ${resp.status}`)
    const data = await resp.json()
    return data.groupCode ?? null
  }

  async checkGroup(groupCode: string): Promise<boolean> {
    const apiUrl = this._signalingUrl.replace('wss://', 'https://').replace('ws://', 'http://') + `/api/group/${groupCode}`
    try {
      const resp = await fetch(apiUrl)
      return resp.ok
    } catch {
      return false
    }
  }

  connect(groupCode: string, deviceName: string, password?: string) {
    this.disconnect()

    if (!this._deviceId) {
      this._deviceId = this.generateDeviceId()
    }
    this._deviceName = deviceName
    this._groupCode = groupCode.toUpperCase()

    const params = new URLSearchParams({
      deviceId: this._deviceId,
      deviceName: this._deviceName,
      lanIp: '',
    })
    if (password) params.set('password', password)

    const wsUrl = `${this._signalingUrl}/ws/group/${this._groupCode}?${params}`

    this.ws = new WebSocket(wsUrl)

    this.ws.onopen = () => {
      this.onConnected?.()
    }

    this.ws.onmessage = (ev) => {
      try {
        const msg: SignalingMessage = JSON.parse(ev.data)
        this.onMessage?.(msg)
      } catch {}
    }

    this.ws.onclose = () => {
      this.onDisconnected?.()
    }

    this.ws.onerror = () => {
      this.onError?.('WebSocket 连接失败')
    }
  }

  disconnect() {
    if (this.reconnectTimer) {
      clearTimeout(this.reconnectTimer)
      this.reconnectTimer = null
    }
    if (this.ws) {
      this.ws.onclose = null
      this.ws.onerror = null
      this.ws.close()
      this.ws = null
    }
    this._groupCode = ''
  }

  send(msg: SignalingMessage) {
    if (this.ws?.readyState !== WebSocket.OPEN) return
    this.ws.send(JSON.stringify(msg))
  }

  sendSdpOffer(targetDeviceId: string, sdp: string) {
    this.send({ type: 'sdp-offer', to: targetDeviceId, sdp })
  }

  sendSdpAnswer(targetDeviceId: string, sdp: string) {
    this.send({ type: 'sdp-answer', to: targetDeviceId, sdp })
  }

  sendIceCandidate(targetDeviceId: string, candidate: string, sdpMid?: string, sdpMlineIndex?: number) {
    this.send({ type: 'ice-candidate', to: targetDeviceId, candidate, sdpMid, sdpMlineIndex })
  }

  sendFileOffer(targetDeviceId: string, task: { fileId: string; fileName: string; fileSize: number; chunkSize: number; totalChunks: number; sha256: string }) {
    this.send({ type: 'file-offer', to: targetDeviceId, ...task })
  }

  sendFileAccept(targetDeviceId: string, fileId: string) {
    this.send({ type: 'file-accept', to: targetDeviceId, fileId })
  }

  sendFileReject(targetDeviceId: string, fileId: string) {
    this.send({ type: 'file-reject', to: targetDeviceId, fileId })
  }
}

export const signalingService = new SignalingService()
