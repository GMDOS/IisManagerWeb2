/**
 * Scripts para acesso ao sistema de arquivos usando a File System Access API
 */

// Variável global para armazenar o handle da pasta raiz
let rootDirectoryHandle = null;

// Função para selecionar uma pasta
window.selectFolder = async function () {
    try {
        // Verifica se o navegador suporta a API
        if (!('showDirectoryPicker' in window)) {
            console.error('File System Access API não suportada neste navegador');
            return { success: false };
        }
        
        // Abre o seletor de diretório
        const dirHandle = await window.showDirectoryPicker();
        // Armazena o handle globalmente para reutilização
        rootDirectoryHandle = dirHandle;
        
        const files = [];
        const folderMap = {}; // Mapeia caminhos relativos para handles
        let fileCount = 0;
        
        // Recursivamente percorre os arquivos e diretórios
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
        // Armazena o handle do diretório para acesso posterior
        folderMap[path || '/'] = JSON.stringify({ type: 'directory', handle: dirHandle.name });
        
        // Adicionamos o diretório à lista de arquivos também, para que o servidor saiba que precisa criá-lo
        // if (path) {
        //     files.push({
        //         relativePath: path,
        //         fileName: dirHandle.name,
        //         size: 0,
        //         lastModified: new Date(),
        //         isDirectory: true
        //     });
        // }
        
        // Percorre os arquivos e diretórios
        for await (const entry of dirHandle.values()) {
            const relativePath = path ? `${path}/${entry.name}` : entry.name;
            
            if (entry.kind === 'file') {
                try {
                    // Obtém informações sobre o arquivo
                    const file = await entry.getFile();
                    files.push({
                        relativePath: relativePath,
                        fileName: file.name,
                        size: file.size,
                        lastModified: new Date(file.lastModified),
                        isDirectory: false
                    });
                    
                    // Armazena o handle do arquivo
                    folderMap[relativePath] = JSON.stringify({ 
                        type: 'file', 
                        dirHandle: dirHandle.name,
                        fileName: file.name
                    });
                } catch (error) {
                    console.error(`Erro ao processar arquivo ${relativePath}:`, error);
                }
            } else if (entry.kind === 'directory') {
                // Recursivamente escaneia subdiretórios
                await scanDirectory(entry, relativePath, files, folderMap);
            }
        }
    } catch (error) {
        console.error(`Erro ao escanear diretório ${path}:`, error);
    }
}

// Função para ler o conteúdo de um arquivo a partir do mapa de pastas
window.readFileFromFolder = async function (filePath, folderMap) {
    try {
        const fileInfo = JSON.parse(folderMap[filePath]);
        
        if (fileInfo.type !== 'file') {
            throw new Error('O caminho não corresponde a um arquivo');
        }
        
        // Para cada nível de diretório, abrimos o handle
        const pathParts = filePath.split('/');
        const fileName = pathParts.pop(); // Último elemento é o nome do arquivo
        
        // Verificamos se já temos o handle do diretório raiz armazenado
        if (!rootDirectoryHandle) {
            throw new Error('Handle do diretório raiz não encontrado. Por favor, selecione a pasta novamente.');
        }
        
        // Começamos do diretório raiz já armazenado
        let currentDirHandle = rootDirectoryHandle;
        
        // Navegamos para cada subdiretório no caminho
        let dirPath = '';
        for (let i = 0; i < pathParts.length; i++) {
            const part = pathParts[i];
            dirPath = dirPath ? `${dirPath}/${part}` : part;
            currentDirHandle = await currentDirHandle.getDirectoryHandle(part);
        }
        
        // Agora que estamos no diretório correto, pegamos o arquivo
        const fileHandle = await currentDirHandle.getFileHandle(fileName);
        const file = await fileHandle.getFile();
        
        // Lê o arquivo como um array de bytes
        return new Uint8Array(await file.arrayBuffer());
    } catch (error) {
        console.error(`Erro ao ler arquivo ${filePath}:`, error);
        throw error;
    }
}; 