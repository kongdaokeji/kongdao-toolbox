addEventListener('error',event=>window.chrome?.webview?.postMessage({type:'error',message:event.message}));
addEventListener('unhandledrejection',event=>window.chrome?.webview?.postMessage({type:'error',message:String(event.reason)}));
