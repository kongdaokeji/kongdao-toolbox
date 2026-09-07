export interface SignalingMessage {
  type: string
  from?: string
  to?: string
  sdp?: string
  candidate?: string
  sdpMid?: string
  sdpMlineIndex?: number
  fileId?: string
  fileName?: string
  fileSize?: number
  chunkSize?: number
  totalChunks?: number
  sha256?: string
  deviceId?: string
  deviceName?: string
  lanIp?: string
  joinedAt?: number
  groupCode?: string
  groupName?: string
  password?: string
  errorMessage?: string
  timestamp?: number
  peers?: PeerInfo[]
  device?: PeerInfo
}

export interface PeerInfo {
  deviceId: string
  deviceName: string
  lanIp: string | null
  joinedAt: number
}

export interface GroupDevice {
  deviceId: string
  deviceName: string
  lanIp: string | null
  isOnline: boolean
  connectionType: ConnectionType
  joinedAt: number
}

export type ConnectionType = 'lan' | 'webrtc-p2p' | 'webrtc-turn' | 'ws-relay'

export type TransferDirection = 'sending' | 'receiving'

export type TransferStatus =
  | 'pending'
  | 'connecting'
  | 'transferring'
  | 'paused'
  | 'completed'
  | 'failed'
  | 'cancelled'

export interface FileTransferTask {
  fileId: string
  fileName: string
  fileSize: number
  sha256: string
  chunkSize: number
  totalChunks: number
  completedChunks: number
  direction: TransferDirection
  status: TransferStatus
  connectionType: ConnectionType
  fromDeviceId: string
  fromDeviceName: string
  toDeviceId: string
  speedMbps: number
  bytesTransferred: number
  startTime: number
  errorMessage: string
  file?: File
}

export interface TransferGroup {
  groupId: string
  groupName: string
  password: string
  creatorDeviceId: string
  createdAt: number
  devices: GroupDevice[]
}
