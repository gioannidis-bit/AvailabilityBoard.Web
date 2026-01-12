window.HIT_typeaheadEmployee = function (opts) {
    const input = opts.input;
    const hidden = opts.hidden;
    const box = opts.box;
    const min = opts.minChars ?? 2;

    let timer = null;
    let last = "";

    function clearBox() {
        box.innerHTML = "";
        box.classList.add("d-none");
    }

    async function fetchData(q) {
        const res = await fetch(`/api/employees/search?q=${encodeURIComponent(q)}`, {
            headers: { "Accept": "application/json" }
        });
        if (!res.ok) return [];
        return await res.json();
    }

    function render(items) {
        box.innerHTML = "";
        if (!items.length) {
            clearBox();
            return;
        }
        box.classList.remove("d-none");

        for (const it of items) {
            const div = document.createElement("div");
            div.className = "list-group-item list-group-item-action py-2";
            div.style.cursor = "pointer";
            div.innerHTML = `
        <div class="fw-semibold">${it.name}</div>
        <div class="small text-muted">${it.sam}${it.email ? " · " + it.email : ""}</div>
      `;
            div.addEventListener("click", () => {
                input.value = `${it.name} (${it.sam})`;
                hidden.value = it.id;
                clearBox();
            });
            box.appendChild(div);
        }
    }

    input.addEventListener("input", () => {
        const q = input.value.trim();
        if (q.length < min) {
            clearBox();
            return;
        }
        if (q === last) return;
        last = q;

        if (timer) clearTimeout(timer);
        timer = setTimeout(async () => {
            const items = await fetchData(q);
            render(items);
        }, 200);
    });

    // click outside closes
    document.addEventListener("click", (e) => {
        if (!box.contains(e.target) && e.target !== input) clearBox();
    });

    // Esc closes
    input.addEventListener("keydown", (e) => {
        if (e.key === "Escape") clearBox();
    });

    clearBox();
};
