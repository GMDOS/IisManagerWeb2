/**
 * Scripts para acesso ao sistema de arquivos usando a File System Access API
 */

// Variável global para armazenar o handle da pasta raiz
let rootDirectoryHandle = null;

// Função para selecionar uma pasta
window.selectFolder = async function () {
    try {
        if (!('showDirectoryPicker' in window)) {
            console.error('File System Access API não suportada neste navegador');
            return { success: false };
        }
        
        const dirHandle = await window.showDirectoryPicker();
        rootDirectoryHandle = dirHandle;
        
        const files = [];
        const folderMap = {}; // Mapeia caminhos relativos para handles
        let fileCount = 0;
        
        await scanDirectory(dirHandle, '', files, folderMap);
        
        fileCount = files.length;
        
        return {
            success: true,
            folderName: dirHandle.name,
            fileCount: fileCount,
            files: files,
            folderMap: folderMap
        };
    } catch (error) {
        console.error('Erro ao selecionar pasta:', error);
        return { success: false };
    }
};

// Função recursiva para escanear diretórios
async function scanDirectory(dirHandle, path, files, folderMap) {
    try {
        folderMap[path || '/'] = JSON.stringify({ type: 'directory', handle: dirHandle.name });
        
        for await (const entry of dirHandle.values()) {
            const relativePath = path ? `${path}/${entry.name}` : entry.name;
            
            if (entry.kind === 'file') {
                try {
                    const file = await entry.getFile();
                    files.push({
                        relativePath: relativePath,
                        fileName: file.name,
                        size: file.size,
                        lastModified: new Date(file.lastModified),
                        isDirectory: false
                    });
                    
                    folderMap[relativePath] = JSON.stringify({ 
                        type: 'file', 
                        dirHandle: dirHandle.name,
                        fileName: file.name
                    });
                } catch (error) {
                    console.error(`Erro ao processar arquivo ${relativePath}:`, error);
                }
            } else if (entry.kind === 'directory') {
                await scanDirectory(entry, relativePath, files, folderMap);
            }
        }
    } catch (error) {
        console.error(`Erro ao escanear diretório ${path}:`, error);
    }
}

window.readFileFromFolder = async function (filePath, folderMap) {
    try {
        const fileInfo = JSON.parse(folderMap[filePath]);
        
        if (fileInfo.type !== 'file') {
            throw new Error('O caminho não corresponde a um arquivo');
        }
        
        const pathParts = filePath.split('/');
        const fileName = pathParts.pop();
        
        if (!rootDirectoryHandle) {
            throw new Error('Handle do diretório raiz não encontrado. Por favor, selecione a pasta novamente.');
        }
        
        let currentDirHandle = rootDirectoryHandle;
        
        let dirPath = '';
        for (let i = 0; i < pathParts.length; i++) {
            const part = pathParts[i];
            dirPath = dirPath ? `${dirPath}/${part}` : part;
            currentDirHandle = await currentDirHandle.getDirectoryHandle(part);
        }
        
        const fileHandle = await currentDirHandle.getFileHandle(fileName);
        const file = await fileHandle.getFile();
        
        return new Uint8Array(await file.arrayBuffer());
    } catch (error) {
        console.error(`Erro ao ler arquivo ${filePath}:`, error);
        throw error;
    }
}; 