<template>
  <v-app>
    <v-app-bar color="primary" elevation="2">
      <template v-slot:prepend>
        <v-icon icon="mdi-swap-horizontal-bold" size="28" />
      </template>
      <v-app-bar-title>
        <span class="text-h6 font-weight-bold">文件传输助手</span>
        <span class="text-caption ml-2 opacity-70">{{ statusLabel }}</span>
      </v-app-bar-title>
      <template v-slot:append>
        <v-btn icon="mdi-cog" @click="settingsDialog = true" />
      </template>
    </v-app-bar>

    <v-main>
      <v-container class="pa-4" style="max-width: 960px">

        <v-row dense>
          <v-col cols="12" sm="6">
            <v-card v-if="!state.group" variant="outlined" class="fill-height">
              <v-card-title class="d-flex align-center">
                <v-icon icon="mdi-account-group" class="mr-2" />
                创建或加入群组
              </v-card-title>
              <v-card-text>
                <v-text-field
                  v-model="state.deviceName"
                  label="设备名称"
                  variant="outlined"
                  density="compact"
                  prepend-inner-icon="mdi-monitor"
                  class="mb-3"
                />
                <v-divider class="mb-3" />
                <div class="text-subtitle-2 mb-2">创建群组</div>
                <v-text-field
                  v-model="createGroupName"
                  label="群组名称"
                  variant="outlined"
                  density="compact"
                  placeholder="我的传输群组"
                  class="mb-2"
                />
                <v-text-field
                  v-model="createPassword"
                  label="密码 (可选)"
                  variant="outlined"
                  density="compact"
                  type="password"
                  class="mb-2"
                />
                <v-btn
                  color="primary"
                  block
                  :loading="creating"
                  @click="handleCreateGroup"
                >
                  <v-icon icon="mdi-plus" start /> 创建群组
                </v-btn>

                <v-divider class="my-4" />

                <div class="text-subtitle-2 mb-2">加入群组</div>
                <v-text-field
                  v-model="joinCode"
                  label="群组码"
                  variant="outlined"
                  density="compact"
                  placeholder="输入6位群组码"
                  maxlength="6"
                  class="mb-2"
                  @input="joinCode = joinCode.toUpperCase()"
                />
                <v-text-field
                  v-model="joinPassword"
                  label="密码 (如有)"
                  variant="outlined"
                  density="compact"
                  type="password"
                  class="mb-2"
                />
                <v-btn
                  variant="outlined"
                  block
                  :loading="joining"
                  @click="handleJoinGroup"
                >
                  <v-icon icon="mdi-login" start /> 加入群组
                </v-btn>
              </v-card-text>
            </v-card>

            <v-card v-else variant="outlined" class="fill-height">
              <v-card-title class="d-flex align-center">
                <v-icon icon="mdi-account-group" class="mr-2" />
                当前群组
              </v-card-title>
              <v-card-text>
                <div class="d-flex align-center mb-3">
                  <v-chip size="large" variant="tonal" color="primary" class="text-mono font-weight-bold text-h6" style="letter-spacing: 4px">
                    {{ state.group.groupId }}
                  </v-chip>
                  <v-btn icon="mdi-content-copy" variant="text" size="small" class="ml-2" @click="copyGroupCode" />
                </div>
                <div class="text-body-2 mb-1">
                  <span class="text-medium-emphasis">群组名:</span> {{ state.group.groupName }}
                </div>
                <v-btn color="error" variant="tonal" block class="mt-3" @click="leaveGroup">
                  <v-icon icon="mdi-logout" start /> 离开群组
                </v-btn>
              </v-card-text>
            </v-card>
          </v-col>

          <v-col cols="12" sm="6">
            <v-card variant="outlined" class="fill-height">
              <v-card-title class="d-flex align-center">
                <v-icon icon="mdi-devices" class="mr-2" />
                在线设备
                <v-chip size="small" class="ml-2" v-if="onlineDevices.length">
                  {{ onlineDevices.length }}
                </v-chip>
              </v-card-title>
              <v-card-text>
                <div v-if="onlineDevices.length === 0" class="text-center py-6">
                  <v-icon icon="mdi-lan-disconnect" size="48" color="grey" />
                  <div class="text-body-2 text-medium-emphasis mt-2">暂无其他在线设备</div>
                  <div class="text-caption text-disabled">加入群组后其他设备会显示在这里</div>
                </div>
                <v-list v-else density="compact">
                  <v-list-item v-for="device in onlineDevices" :key="device.deviceId">
                    <template v-slot:prepend>
                      <v-avatar color="surface-variant" size="36">
                        <v-icon icon="mdi-monitor" />
                      </v-avatar>
                    </template>
                    <v-list-item-title>{{ device.deviceName }}</v-list-item-title>
                    <v-list-item-subtitle>{{ device.lanIp ?? '远程' }}</v-list-item-subtitle>
                    <template v-slot:append>
                      <v-chip size="x-small" :color="device.connectionType === 'lan' ? 'success' : 'primary'" variant="tonal">
                        {{ device.connectionType === 'lan' ? '局域网' : 'P2P' }}
                      </v-chip>
                    </template>
                  </v-list-item>
                </v-list>
              </v-card-text>
            </v-card>
          </v-col>
        </v-row>

        <v-card variant="outlined" class="mt-4">
          <v-card-title class="d-flex align-center">
            <v-icon icon="mdi-file-send" class="mr-2" />
            发送文件
          </v-card-title>
          <v-card-text>
            <v-sheet
              :color="dragOver ? 'primary' : 'surface-variant'"
              rounded="lg"
              class="d-flex flex-column align-center justify-center pa-8"
              :class="{ 'on-primary': dragOver }"
              @dragover.prevent="dragOver = true"
              @dragleave="dragOver = false"
              @drop.prevent="handleDrop"
              @click="triggerFilePicker"
              style="cursor: pointer; border: 2px dashed; transition: all 0.2s"
            >
              <v-icon :icon="dragOver ? 'mdi-file-download' : 'mdi-cloud-upload'" size="48" class="mb-2" />
              <div class="text-body-1">{{ dragOver ? '松开发送' : '拖拽文件到此处' }}</div>
              <div class="text-caption text-medium-emphasis">或点击选择文件</div>
            </v-sheet>
            <input ref="fileInput" type="file" multiple style="display: none" @change="handleFileSelect" />
            <div v-if="state.group && onlineDevices.length > 0" class="mt-3 d-flex ga-2">
              <v-btn color="primary" @click="triggerFilePicker">
                <v-icon icon="mdi-paperclip" start /> 选择文件
              </v-btn>
              <v-btn variant="outlined" @click="triggerFilePickerForAll">
                <v-icon icon="mdi-send" start /> 发送给所有人
              </v-btn>
            </div>
          </v-card-text>
        </v-card>

        <v-card variant="outlined" class="mt-4">
          <v-card-title class="d-flex align-center">
            <v-icon icon="mdi-transfer" class="mr-2" />
            传输列表
            <v-chip size="small" class="ml-2" v-if="state.transfers.length > 0">
              {{ state.transfers.length }}
            </v-chip>
          </v-card-title>
          <v-card-text class="pa-0">
            <div v-if="state.transfers.length === 0" class="text-center py-8">
              <v-icon icon="mdi-inbox-arrow-down" size="48" color="grey" />
              <div class="text-body-2 text-medium-emphasis mt-2">暂无传输任务</div>
              <div class="text-caption text-disabled">加入群组后即可开始传输文件</div>
            </div>
            <v-list v-else density="compact">
              <v-list-item v-for="task in state.transfers" :key="task.fileId" class="px-4">
                <template v-slot:prepend>
                  <v-icon
                    :icon="task.direction === 'sending' ? 'mdi-upload' : 'mdi-download'"
                    :color="task.direction === 'sending' ? 'primary' : 'success'"
                    class="mr-3"
                  />
                </template>
                <v-list-item-title class="d-flex align-center">
                  <span class="text-truncate" style="max-width: 200px">{{ task.fileName }}</span>
                  <v-chip size="x-small" class="ml-2" variant="tonal">
                    {{ formatFileSize(task.fileSize) }}
                  </v-chip>
                  <v-chip size="x-small" class="ml-1" :color="statusColor(task.status)" variant="tonal">
                    {{ statusText(task.status) }}
                  </v-chip>
                </v-list-item-title>
                <v-list-item-subtitle>
                  <v-progress-linear
                    :model-value="task.completedChunks && task.totalChunks ? (task.completedChunks / task.totalChunks) * 100 : 0"
                    :color="task.status === 'failed' ? 'error' : task.status === 'completed' ? 'success' : 'primary'"
                    height="4"
                    rounded
                    class="mt-1 mb-1"
                  />
                  <span class="text-caption">
                    {{ formatFileSize(task.bytesTransferred) }} / {{ formatFileSize(task.fileSize) }}
                    <span v-if="task.speedMbps > 0" class="ml-2">{{ formatSpeed(task.speedMbps) }}</span>
                    <span v-if="task.completedChunks && task.totalChunks" class="ml-2">
                      ({{ task.completedChunks }}/{{ task.totalChunks }})
                    </span>
                  </span>
                </v-list-item-subtitle>
                <template v-slot:append>
                  <v-chip size="x-small" variant="tonal">
                    {{ task.connectionType === 'lan' ? '局域网' : 'P2P' }}
                  </v-chip>
                </template>
              </v-list-item>
            </v-list>
          </v-card-text>
        </v-card>

      </v-container>

      <v-dialog v-model="settingsDialog" max-width="480">
        <v-card>
          <v-card-title>信令服务器设置</v-card-title>
          <v-card-text>
            <v-text-field
              v-model="state.signalingUrl"
              label="信令服务器 URL"
              variant="outlined"
              placeholder="wss://your-worker.workers.dev"
            />
          </v-card-text>
          <v-card-actions>
            <v-spacer />
            <v-btn @click="settingsDialog = false">关闭</v-btn>
          </v-card-actions>
        </v-card>
      </v-dialog>

      <v-dialog v-model="targetDialog" max-width="400">
        <v-card>
          <v-card-title>选择目标设备</v-card-title>
          <v-card-text>
            <v-list density="compact" v-model:selected="selectedTargets">
              <v-list-item v-for="device in onlineDevices" :key="device.deviceId" :value="device.deviceId">
                <template v-slot:prepend>
                  <v-icon icon="mdi-monitor" />
                </template>
                <v-list-item-title>{{ device.deviceName }}</v-list-item-title>
              </v-list-item>
            </v-list>
          </v-card-text>
          <v-card-actions>
            <v-btn @click="targetDialog = false">取消</v-btn>
            <v-btn color="primary" :disabled="selectedTargets.length === 0" @click="confirmSendToTarget">发送</v-btn>
          </v-card-actions>
        </v-card>
      </v-dialog>

      <v-snackbar
        v-for="toast in state.toasts"
        :key="toast.id"
        :color="toast.color"
        :model-value="true"
        :timeout="4000"
        location="top"
      >
        {{ toast.msg }}
      </v-snackbar>
    </v-main>
  </v-app>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import { useFileTransfer } from './composables/useFileTransfer'
import { signalingService } from './services/signaling'

const {
  state,
  createGroup,
  joinGroup,
  leaveGroup,
  sendFile,
  sendFileToAll,
  copyGroupCode,
  formatFileSize,
  formatSpeed,
} = useFileTransfer()

const createGroupName = ref('')
const createPassword = ref('')
const joinCode = ref('')
const joinPassword = ref('')
const creating = ref(false)
const joining = ref(false)
const dragOver = ref(false)
const settingsDialog = ref(false)
const targetDialog = ref(false)
const selectedTargets = ref<string[]>([])
const pendingFile = ref<File | null>(null)
const sendAll = ref(false)
const fileInput = ref<HTMLInputElement>()

const statusLabel = computed(() => {
  if (!state.group) return '未加入群组'
  if (!state.connected) return '连接中...'
  return `群组 ${state.group.groupId}`
})

const onlineDevices = computed(() =>
  state.devices.filter(d => d.isOnline && d.deviceId !== signalingService.deviceId)
)

function statusText(s: string) {
  const map: Record<string, string> = {
    pending: '等待中', connecting: '连接中', transferring: '传输中',
    paused: '已暂停', completed: '已完成', failed: '失败', cancelled: '已取消',
  }
  return map[s] ?? s
}

function statusColor(s: string) {
  const map: Record<string, string> = {
    pending: 'grey', connecting: 'info', transferring: 'primary',
    completed: 'success', failed: 'error', cancelled: 'warning',
  }
  return map[s] ?? 'grey'
}

async function handleCreateGroup() {
  creating.value = true
  try {
    await createGroup(createGroupName.value || '我的传输群组', createPassword.value || undefined)
  } finally {
    creating.value = false
  }
}

async function handleJoinGroup() {
  if (joinCode.value.length !== 6) {
    return
  }
  joining.value = true
  try {
    await joinGroup(joinCode.value, joinPassword.value || undefined)
  } finally {
    joining.value = false
  }
}

function triggerFilePicker() {
  fileInput.value?.click()
}

function triggerFilePickerForAll() {
  sendAll.value = true
  fileInput.value?.click()
}

function handleFileSelect(ev: Event) {
  const input = ev.target as HTMLInputElement
  if (!input.files?.length) return
  const file = input.files[0]
  input.value = ''

  if (sendAll.value) {
    sendAll.value = false
    sendFileToAll(file)
  } else if (onlineDevices.value.length === 1) {
    sendFile(onlineDevices.value[0].deviceId, file)
  } else if (onlineDevices.value.length > 1) {
    pendingFile.value = file
    selectedTargets.value = []
    targetDialog.value = true
  }
}

function handleDrop(ev: DragEvent) {
  dragOver.value = false
  const file = ev.dataTransfer?.files[0]
  if (!file) return

  if (onlineDevices.value.length === 1) {
    sendFile(onlineDevices.value[0].deviceId, file)
  } else if (onlineDevices.value.length > 1) {
    pendingFile.value = file
    selectedTargets.value = []
    targetDialog.value = true
  }
}

function confirmSendToTarget() {
  if (selectedTargets.value.length > 0 && pendingFile.value) {
    sendFile(selectedTargets.value[0], pendingFile.value)
  }
  targetDialog.value = false
  pendingFile.value = null
}
</script>

<style>
body {
  margin: 0;
  overflow-y: auto;
}
.text-mono {
  font-family: 'Consolas', 'Monaco', monospace;
}
</style>
