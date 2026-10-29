import { signalingService } from './signaling'
import type { FileTransferTask } from '../types'

const ICE_SERVERS: RTCConfiguration = {
  iceServers: [
    { urls: 'stun:stun.l.google.com:19302' },
    { urls: 'stun:stun1.l.google.com:19302' },
  ],
}

const CHUNK_SIZE = 16384

class WebRtcService {
  private peers: Map<string, RTCPeerConnection> = new Map()
  private dataChannels: Map<string, RTCDataChannel> = new Map()
  private pendingReceives: Map<string, { chunks: Map<number, Uint8Array>; task: FileTransferTask; received: number }> = new Map()

  onPeerConnected: ((deviceId: string) => void) | null = null
  onPeerDisconnected: ((deviceId: string) => void) | null = null
  onFileOfferReceived: ((task: FileTransferTask) => void) | null = null
  onTransferProgressChanged: ((task: FileTransferTask) => void) | null = null
  onTransferCompleted: ((task: FileTransferTask) => void) | null = null
  onTransferFailed: ((task: FileTransferTask) => void) | null = null

  async createOffer(targetDeviceId: string): Promise<RTCDataChannel> {
    const pc = new RTCPeerConnection(ICE_SERVERS)
    this.peers.set(targetDeviceId, pc)

    const dc = pc.createDataChannel('fileTransfer', { ordered: true, maxRetransmits: 10 })
    this.dataChannels.set(targetDeviceId, dc)

    dc.binaryType = 'arraybuffer'
    this.setupDataChannel(dc, targetDeviceId)

    pc.onicecandidate = (ev) => {
      if (ev.candidate) {
        signalingService.sendIceCandidate(
          targetDeviceId,
          ev.candidate.candidate,
          ev.candidate.sdpMid ?? undefined,
          ev.candidate.sdpMLineIndex ?? 0,
        )
      }
    }

    const offer = await pc.createOffer()
    await pc.setLocalDescription(offer)
    signalingService.sendSdpOffer(targetDeviceId, pc.localDescription!.sdp!)

    return dc
  }

  async handleOffer(fromDeviceId: string, sdp: string): Promise<string> {
    let pc = this.peers.get(fromDeviceId)
    if (pc) {
      pc.close()
      this.peers.delete(fromDeviceId)
    }

    pc = new RTCPeerConnection(ICE_SERVERS)
    this.peers.set(fromDeviceId, pc)

    pc.ondatachannel = (ev) => {
      const dc = ev.channel
      dc.binaryType = 'arraybuffer'
      this.dataChannels.set(fromDeviceId, dc)
      this.setupDataChannel(dc, fromDeviceId)
    }

    pc.onicecandidate = (ev) => {
      if (ev.candidate) {
        signalingService.sendIceCandidate(
          fromDeviceId,
          ev.candidate.candidate,
          ev.candidate.sdpMid ?? undefined,
          ev.candidate.sdpMLineIndex ?? 0,
        )
      }
    }

    await pc.setRemoteDescription(new RTCSessionDescription({ type: 'offer', sdp }))
    const answer = await pc.createAnswer()
    await pc.setLocalDescription(answer)

    return pc.localDescription!.sdp!
  }

  async handleAnswer(fromDeviceId: string, sdp: string) {
    const pc = this.peers.get(fromDeviceId)
    if (!pc) return
    await pc.setRemoteDescription(new RTCSessionDescription({ type: 'answer', sdp }))
  }

  async addIceCandidate(fromDeviceId: string, candidate: string, sdpMid?: string, sdpMlineIndex?: number) {
    const pc = this.peers.get(fromDeviceId)
    if (!pc) return

    await pc.addIceCandidate(new RTCIceCandidate({
      candidate,
      sdpMid: sdpMid ?? '0',
      sdpMLineIndex: sdpMlineIndex ?? 0,
    }))
  }

  private setupDataChannel(dc: RTCDataChannel, deviceId: string) {
    dc.onopen = () => {
      this.onPeerConnected?.(deviceId)
    }
    dc.onclose = () => {
      this.onPeerDisconnected?.(deviceId)
    }
    dc.onmessage = (ev) => {
      if (typeof ev.data === 'string') {
        this.handleDataChannelMessage(deviceId, ev.data)
      }
    }
  }

  private handleDataChannelMessage(fromDeviceId: string, msgStr: string) {
    try {
      const msg = JSON.parse(msgStr)
      switch (msg.type) {
        case 'file-header': {
          const task: FileTransferTask = {
            fileId: msg.fileId,
            fileName: msg.fileName,
            fileSize: msg.fileSize,
            sha256: msg.sha256,
            chunkSize: msg.chunkSize,
            totalChunks: msg.totalChunks,
            completedChunks: 0,
            direction: 'receiving',
            status: 'pending',
            connectionType: 'webrtc-p2p',
            fromDeviceId: msg.fromDeviceId || fromDeviceId,
            fromDeviceName: msg.fromDeviceName || '未知设备',
            toDeviceId: signalingService.deviceId,
            speedMbps: 0,
            bytesTransferred: 0,
            startTime: Date.now(),
            errorMessage: '',
          }
          this.pendingReceives.set(msg.fileId, {
            chunks: new Map(),
            task,
            received: 0,
          })
          this.onFileOfferReceived?.(task)
          break
        }
        case 'file-chunk': {
          const receive = this.pendingReceives.get(msg.fileId)
          if (!receive) return

          const binary = atob(msg.data)
          const bytes = new Uint8Array(binary.length)
          for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i)

          receive.chunks.set(msg.index, bytes)
          receive.received++
          receive.task.completedChunks = receive.received
          receive.task.bytesTransferred = Array.from(receive.chunks.values()).reduce((s, c) => s + c.length, 0)

          const elapsed = (Date.now() - receive.task.startTime) / 1000
          receive.task.speedMbps = elapsed > 0 ? receive.task.bytesTransferred / elapsed / 1024 / 1024 : 0
          receive.task.status = 'transferring'
          this.onTransferProgressChanged?.(receive.task)
          break
        }
        case 'file-eof': {
          const receive = this.pendingReceives.get(msg.fileId)
          if (!receive) return

          const sortedChunks = Array.from(receive.chunks.entries())
            .sort(([a], [b]) => a - b)
            .map(([, v]) => v)

          const blob = new Blob(sortedChunks.map(c => new Uint8Array(c) as BlobPart))
          const url = URL.createObjectURL(blob)
          const a = document.createElement('a')
          a.href = url
          a.download = receive.task.fileName
          a.click()
          URL.revokeObjectURL(url)

          receive.task.status = 'completed'
          receive.task.completedChunks = receive.task.totalChunks
          receive.task.bytesTransferred = receive.task.fileSize
          this.onTransferCompleted?.(receive.task)
          this.pendingReceives.delete(msg.fileId)
          break
        }
      }
    } catch {}
  }

  async sendFile(targetDeviceId: string, file: File, task: FileTransferTask): Promise<void> {
    let dc = this.dataChannels.get(targetDeviceId)
    if (!dc || dc.readyState !== 'open') {
      dc = await this.createOffer(targetDeviceId)
      await new Promise<void>((resolve, reject) => {
        const timeout = setTimeout(() => reject(new Error('连接超时')), 30000)
        const onOpen = () => { clearTimeout(timeout); resolve() }
        const onClose = () => { clearTimeout(timeout); reject(new Error('连接关闭')) }
        dc!.addEventListener('open', onOpen, { once: true })
        dc!.addEventListener('close', onClose, { once: true })
      })
    }

    const totalChunks = Math.ceil(file.size / CHUNK_SIZE)

    const headerMsg = JSON.stringify({
      type: 'file-header',
      fileId: task.fileId,
      fileName: task.fileName,
      fileSize: task.fileSize,
      chunkSize: CHUNK_SIZE,
      totalChunks,
      sha256: task.sha256,
      fromDeviceId: signalingService.deviceId,
      fromDeviceName: 'Web Browser',
    })
    dc.send(headerMsg)

    await new Promise(r => setTimeout(r, 200))

    let offset = 0
    let chunkIndex = 0
    const startTime = Date.now()

    while (offset < file.size) {
      const end = Math.min(offset + CHUNK_SIZE, file.size)
      const slice = file.slice(offset, end)
      const buffer = await slice.arrayBuffer()
      const bytes = new Uint8Array(buffer)

      let binary = ''
      for (let i = 0; i < bytes.length; i++) binary += String.fromCharCode(bytes[i])
      const base64 = btoa(binary)

      const chunkMsg = JSON.stringify({
        type: 'file-chunk',
        fileId: task.fileId,
        index: chunkIndex,
        data: base64,
      })
      dc.send(chunkMsg)

      offset = end
      chunkIndex++

      task.bytesTransferred = offset
      task.completedChunks = chunkIndex
      task.speedMbps = offset / ((Date.now() - startTime) / 1000) / 1024 / 1024
      task.status = 'transferring'

      if (dc.bufferedAmount > 1048576) {
        await new Promise<void>(r => {
          dc!.onbufferedamountlow = () => r()
          dc!.bufferedAmountLowThreshold = 524288
        })
      }
    }

    const eofMsg = JSON.stringify({
      type: 'file-eof',
      fileId: task.fileId,
      totalChunks,
    })
    dc.send(eofMsg)

    task.status = 'completed'
    task.completedChunks = totalChunks
  }

  closePeer(deviceId: string) {
    const dc = this.dataChannels.get(deviceId)
    if (dc) dc.close()
    const pc = this.peers.get(deviceId)
    if (pc) pc.close()
    this.dataChannels.delete(deviceId)
    this.peers.delete(deviceId)
  }

  closeAll() {
    for (const [id] of this.peers) {
      this.closePeer(id)
    }
  }
}

export const webRtcService = new WebRtcService()
