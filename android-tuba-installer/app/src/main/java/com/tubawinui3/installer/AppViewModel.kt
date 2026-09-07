package com.tubawinui3.installer

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.tubawinui3.installer.data.AssetDto
import com.tubawinui3.installer.data.AssetMatcher
import com.tubawinui3.installer.data.CpuArch
import com.tubawinui3.installer.data.DownloadManager
import com.tubawinui3.installer.data.DownloadState
import com.tubawinui3.installer.data.ReleaseDto
import com.tubawinui3.installer.data.ReleaseRepository
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class AppUiState(
    val arch: CpuArch = CpuArch.X64,
    val showGuide: Boolean = false,
    val release: ReleaseDto? = null,
    val source: String? = null,
    val fetching: Boolean = false,
    val fetchError: String? = null,
    val download: DownloadState = DownloadState.Idle,
    val currentAssetName: String? = null,
)

class AppViewModel(application: Application) : AndroidViewModel(application) {

    private val repository = ReleaseRepository()
    private val downloadManager = DownloadManager(application)

    private val _state = MutableStateFlow(AppUiState())
    val state: StateFlow<AppUiState> = _state.asStateFlow()

    /** 下载进度单独暴露，由 DownloadManager 驱动。 */
    val downloadState: StateFlow<DownloadState> = downloadManager.state

    init {
        refresh()
    }

    /** 拉取最新 release（GitCode 镜像优先，GitHub 兜底）。 */
    fun refresh() {
        if (_state.value.fetching) return
        viewModelScope.launch {
            _state.update { it.copy(fetching = true, fetchError = null) }
            try {
                val result = repository.fetchLatest()
                _state.update {
                    it.copy(release = result.release, source = result.source, fetching = false)
                }
            } catch (e: Exception) {
                _state.update {
                    it.copy(fetchError = e.message ?: "获取版本信息失败", fetching = false)
                }
            }
        }
    }

    fun selectArch(arch: CpuArch) {
        _state.update { it.copy(arch = arch) }
    }

    /** 当前架构对应的安装包资产（可能为 null）。 */
    fun currentAsset(): AssetDto? {
        val s = _state.value
        val release = s.release ?: return null
        return AssetMatcher.setupAssetFor(release.assets, s.arch)
    }

    /** 开始下载当前架构的安装包（权限由 UI 侧保证）。 */
    fun startDownload() {
        val asset = currentAsset() ?: return
        _state.update { it.copy(currentAssetName = asset.name) }
        viewModelScope.launch {
            downloadManager.download(asset.browserDownloadUrl, asset.name)
        }
    }

    fun cancelDownload() = downloadManager.cancel()

    fun openGuide() = _state.update { it.copy(showGuide = true) }

    fun closeGuide() = _state.update { it.copy(showGuide = false) }
}
