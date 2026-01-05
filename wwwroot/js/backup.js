export async function downloadJson(filename, jsonText) {
    const blob = new Blob([jsonText], { type: "application/json" });
    const url = URL.createObjectURL(blob);

    const a = document.createElement("a");
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    a.remove();

    URL.revokeObjectURL(url);
}

export async function pickJsonFileText() {
    return await new Promise((resolve, reject) => {
        const input = document.createElement("input");
        input.type = "file";
        input.accept = "application/json";
        input.onchange = async () => {
            try {
                const file = input.files && input.files[0];
                if (!file) return resolve(null);
                resolve(await file.text());
            } catch (e) {
                reject(e);
            }
        };
        input.click();
    });
}

export async function canShareFiles() {
    return !!(navigator.share && navigator.canShare);
}

export async function shareJsonFile(filename, jsonText) {
    const blob = new Blob([jsonText], { type: "application/json" });
    const file = new File([blob], filename, { type: "application/json" });

    if (!navigator.share || !navigator.canShare || !navigator.canShare({ files: [file] })) {
        return false;
    }

    await navigator.share({ files: [file] });
    return true;
}

export async function requestPersistentStorage() {
    if (!("storage" in navigator) || !navigator.storage.persist) return false;
    try {
        return await navigator.storage.persist();
    } catch {
        return false;
    }
}

export async function isPersistentStorage() {
    if (!("storage" in navigator) || !navigator.storage.persisted) return null;
    try {
        return await navigator.storage.persisted();
    } catch {
        return null;
    }
}
