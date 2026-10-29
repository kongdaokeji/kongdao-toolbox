package com.tubawinui3.installer

import android.Manifest
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.platform.LocalContext
import androidx.core.content.ContextCompat
import androidx.lifecycle.viewmodel.compose.viewModel
import com.tubawinui3.installer.ui.DownloadScreen
import com.tubawinui3.installer.ui.GuideScreen
import com.tubawinui3.installer.ui.TubaInstallerTheme

class MainActivity : ComponentActivity() {

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent {
            TubaInstallerTheme {
                val viewModel: AppViewModel = viewModel()
                val state by viewModel.state.collectAsState()
                val download by viewModel.downloadState.collectAsState()
                val context = LocalContext.current

                // Android 9 及以下写公共 Download 需要存储权限
                val storagePermissionLauncher = rememberLauncherForActivityResult(
                    ActivityResultContracts.RequestPermission(),
                ) { granted ->
                    if (granted) viewModel.startDownload()
                }

                if (state.showGuide) {
                    GuideScreen(
                        fileName = state.currentAssetName ?: "",
                        onBack = viewModel::closeGuide,
                    )
                } else {
                    DownloadScreen(
                        state = state,
                        download = download,
                        onArchSelect = viewModel::selectArch,
                        onRefresh = viewModel::refresh,
                        onDownload = {
                            val needLegacyPermission = Build.VERSION.SDK_INT < Build.VERSION_CODES.Q &&
                                ContextCompat.checkSelfPermission(
                                    context,
                                    Manifest.permission.WRITE_EXTERNAL_STORAGE,
                                ) != PackageManager.PERMISSION_GRANTED
                            if (needLegacyPermission) {
                                storagePermissionLauncher.launch(Manifest.permission.WRITE_EXTERNAL_STORAGE)
                            } else {
                                viewModel.startDownload()
                            }
                        },
                        onCancel = viewModel::cancelDownload,
                        onOpenGuide = viewModel::openGuide,
                    )
                }
            }
        }
    }
}
