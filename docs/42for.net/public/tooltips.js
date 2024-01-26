var modal;

function onDocumentLoaded() {
    const mainContent = document.getElementById('VPContent');
    mainContent.addEventListener('click', onContentClick);
    const config = { attributes: false, childList: true, subtree: true };

    const callback = (mutationList, observer) => {
        const docs = mainContent.getElementsByClassName('vp-doc');

        if (docs.length < 1) {
            return;
        }

        if (docs[0].classList.contains('_platform_overview')) {
            prepareElementsForTooltips(docs[0]);
        }
    };

    const observer = new MutationObserver(callback);
    observer.observe(mainContent, config);
}

function prepareElementsForTooltips(content) {
    modal = document.getElementById("tooltip-modal");
    var diagrams = content.getElementsByClassName('mermaid');

    Array.prototype.forEach.call(diagrams, diagram => {
        var nodes = diagram.getElementsByClassName('node');
        Array.prototype.forEach.call(nodes, node => {
            node.classList.add('clickable');
            node.addEventListener('click', onDiagramEntityClick);
        });
    });
}

function onDiagramEntityClick(event) {
    var node = event.currentTarget;
    var id = node.id;
    var content;

    if (id.includes('subject')) {
        content = document.getElementById('modal-content-subject');
    }
    else if (id.includes('context')) {
        content = document.getElementById('modal-content-context');
    }
    else if (id.includes('responsibility')) {
        content = document.getElementById('modal-content-responsibility');
    }
    else if (id.includes('usage')) {
        content = document.getElementById('modal-content-usage');
    }
    else if (id.includes('execution')) {
        content = document.getElementById('modal-content-execution');
    }
    else if (id.includes('scheduled')) {
        content = document.getElementById('modal-content-unit-of-execution');
    }
    else if (id.includes('unit')) {
        content = document.getElementById('modal-content-unit');
    }

    Array.prototype.forEach.call(modal.children[0].children[0].children, content => {
        content.classList.add('invisible');
    });

    const mainContent = document.getElementById('VPContent');
    const main = mainContent.getElementsByClassName('content')[1];
    const sizes = main.getClientRects()[0];
    
    const contentModal = modal.children[0];
    contentModal.style.marginLeft = Math.floor(sizes.x) + 'px';
    contentModal.style.width = Math.floor(sizes.width) + 'px';

    content.classList.remove('invisible');
    modal.style.display = 'block';
    event.stopPropagation();
}

function onContentClick(event) {
    if (event.target == modal) {
        modal.style.display = 'none';
      }
}

if (document.readyState === "loading") {
    document.addEventListener('DOMContentLoaded', onDocumentLoaded);
} else {
    onDocumentLoaded();
}
