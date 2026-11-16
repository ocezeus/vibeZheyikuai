/* 上班工具箱 · 5 个整体界面主题方向 —— 切主题 / 填充色卡 / 时钟 */
(function () {
  var ORDER = ['quiet', 'mica', 'paper', 'instrument', 'neon'];

  var META = {
    quiet: {
      idx: '01', name: '无垠', en: 'Quiet',
      tag: '去掉装饰，只留层次和留白 —— 近黑底 + <b>1px 极细线</b> + 等宽数字。目标是让它<b>「几乎不存在」</b>，一整天盯着也不累。',
      pal: ['#0B0E14', '#2C3442', '#96A9FF', '#E9ECF1'],
      specs: '圆角 <b>10</b> · 描边 <b>1px / 8%</b> · 进度 <b>2px 实线</b><br>字体 <b>苹方 + Consolas</b> · 无光效 · 无投影',
      cost: 'low', costText: '低', sel: 'graphite', curName: '经典 · 石墨灰'
    },
    mica: {
      idx: '02', name: '云母', en: 'Mica',
      tag: '桌面从卡片后面透进来，像一块<b>磨砂玻璃</b>。半透明 + 柔光雾气 + 高光边，放在壁纸上最有「桌面感」，和飞书窗口并排也好看。',
      pal: ['#4E8CFF', '#8CC8FF', '#2A3550', '#F3F7FF'],
      specs: '圆角 <b>18</b> · 玻璃描边 <b>16%</b> · 进度 <b>5px 发光胶囊</b><br>背景模糊 + 顶部高光 · Frost.cs 已有基础',
      cost: 'mid', costText: '中', sel: 'midnight', curName: '经典 · 午夜蓝'
    },
    paper: {
      idx: '03', name: '宣纸', en: 'Ink',
      tag: '宣纸底、墨色字、<b>朱砂一点</b>。白天挨着飞书最舒服；宋体标题 + 方形印章，是五套里唯一主打「东方」的。',
      pal: ['#FDFAF2', '#EFE6D4', '#B23A2C', '#35544F'],
      specs: '圆角 <b>5</b> · 纸纹颗粒 · 进度 <b>3px 笔触</b><br>宋体标题（Noto Serif SC）· 朱砂印章',
      cost: 'mid', costText: '中', sel: 'ivory', curName: '经典 · 象牙白'
    },
    instrument: {
      idx: '04', name: '仪表', en: 'Instrument',
      tag: '把倒计时做成一台真的<b>仪表</b>：金属壳、琥珀数码、<b>分刻刻度进度</b>、四角螺丝。最有「装置感」，一眼看出还剩多少。',
      pal: ['#1E1F1A', '#5E5F4C', '#FFB02E', '#EDE7D6'],
      specs: '圆角 <b>6</b> · 内阴影浮雕 · 进度 <b>24 格分刻</b><br>数字发光（复用 CardGlow 模糊）· 四角螺丝',
      cost: 'high', costText: '中高', sel: 'sunset', curName: '经典 · 落日橙'
    },
    neon: {
      idx: '05', name: '霓虹', en: 'Neon',
      tag: '深空底上的<b>双色霓虹</b>（品红 × 青），带网格和辉光。夜班、加班最有气氛；白天用也不至于太吵。',
      pal: ['#0D0A1A', '#3A1E4E', '#FF3CAC', '#38E8FF'],
      specs: '圆角 <b>14</b> · 渐变描边 · 进度 <b>发光渐变</b><br>数字 / 边框 / 进度三层辉光',
      cost: 'high', costText: '高', sel: 'zi-fengxin', curName: '紫 · 凤信'
    }
  };

  var GRID = ['midnight', 'aurora', 'forest', 'graphite', 'ivory', 'sunset',
              'sakura', 'zi-fengxin', 'lan-yuming', 'huang-liuli', 'hong-fengye', 'bai-yudu'];
  var BUB = ['graphite', 'midnight', 'aurora', 'forest', 'sunset', 'sakura',
             'ivory', 'zi-fengxin', 'zi-tengluo', 'lan-yuming', 'huang-liuli', 'hong-fengye'];

  var BY_ID = {};
  (window.WC_THEMES || []).forEach(function (t) { BY_ID[t.id] = t; });

  function shortName(n) {
    var i = n.indexOf('·');
    return i >= 0 ? n.slice(i + 1).trim() : n;
  }

  function esc(s) {
    return String(s).replace(/[&<>"]/g, function (c) {
      return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c];
    });
  }

  /* ---------------------------------------------------------- 顶栏标签 --- */
  var tabs = document.getElementById('tabs');
  ORDER.forEach(function (k) {
    var m = META[k];
    var b = document.createElement('button');
    b.className = 'tab';
    b.dataset.t = k;
    b.innerHTML = '<b>' + m.idx + '</b>' + m.name;
    b.addEventListener('click', function () { applyTheme(k); });
    tabs.appendChild(b);
  });

  /* ---------------------------------------------------------- 色卡填充 --- */
  function tileHtml(id, sel, withName) {
    var t = BY_ID[id];
    if (!t) return '';
    var style = '--t1:' + t.bgTop + ';--t2:' + t.bgBottom + ';--ta:' + t.accent + ';';
    var on = id === sel ? ' on' : '';
    if (withName) {
      style += '--t-tx:' + t.textMain + ';';
      return '<div class="btile' + on + '" style="' + style + '" title="' + esc(t.name) + '">' +
             '<span>' + esc(shortName(t.name)) + '</span><i></i></div>';
    }
    return '<div class="tile' + on + '" style="' + style + '" title="' + esc(t.name) + '"><u></u><i></i></div>';
  }

  function fillPalettes(sel) {
    var grid = document.getElementById('themeGrid');
    if (grid) grid.innerHTML = GRID.map(function (id) { return tileHtml(id, sel, false); }).join('');
    var bg = document.getElementById('bubGrid');
    if (bg) bg.innerHTML = BUB.map(function (id) { return tileHtml(id, sel, true); }).join('');
  }

  /* ------------------------------------------------------------ 切主题 --- */
  var cur = null;

  function applyTheme(k) {
    if (!META[k]) k = 'quiet';
    cur = k;
    document.body.dataset.theme = k;

    var m = META[k];
    Array.prototype.forEach.call(document.querySelectorAll('.tab'), function (t) {
      t.classList.toggle('on', t.dataset.t === k);
    });

    document.getElementById('tIdx').textContent = m.idx;
    document.getElementById('tName').textContent = m.name;
    document.getElementById('tEn').textContent = m.en;
    document.getElementById('tTag').innerHTML = m.tag;

    document.getElementById('tPal').innerHTML = m.pal.map(function (c) {
      return '<i style="background:' + c + '"></i>';
    }).join('');

    document.getElementById('tSpecs').innerHTML = m.specs;

    var cost = document.getElementById('tCost');
    cost.className = 'cost ' + m.cost;
    cost.innerHTML = '<i></i><span>实现成本</span> <b>' + m.costText + '</b>';

    var curEl = document.getElementById('curTheme');
    if (curEl) curEl.textContent = m.curName;

    fillPalettes(m.sel);

    if (location.hash.slice(1) !== k) {
      try { history.replaceState(null, '', '#' + k); } catch (e) { }
    }
  }

  /* -------------------------------------------------------- 收起态对比 --- */
  function buildCompare() {
    var flex = document.getElementById('cmpFlex');
    if (!flex) return;
    flex.innerHTML = ORDER.map(function (k) {
      var m = META[k];
      return '<div class="cmpitem" data-theme="' + k + '" data-t="' + k + '">' +
        '<div class="lbl"><i></i><b>' + m.idx + ' ' + m.name + '</b>' + m.en + '</div>' +
        '<div class="cardmini">' +
          '<div class="r1"><span class="dot"></span><span class="st">距离下班</span><span class="rt">下班 18:00</span></div>' +
          '<div class="big">05:30:56</div>' +
          '<div class="bar"><i></i></div>' +
        '</div>' +
      '</div>';
    }).join('');

    Array.prototype.forEach.call(flex.querySelectorAll('.cmpitem'), function (it) {
      it.addEventListener('click', function () { applyTheme(it.dataset.t); });
    });
  }

  /* -------------------------------------------------------------- 时钟 --- */
  function fmt(s) {
    var h = Math.floor(s / 3600), m = Math.floor(s % 3600 / 60), sec = s % 60;
    return ('0' + h).slice(-2) + ':' + ('0' + m).slice(-2) + ':' + ('0' + sec).slice(-2);
  }

  var tBig = 3 * 3600 + 7 * 60 + 33;
  var tSmall = 5 * 3600 + 30 * 60 + 56;

  function tick() {
    tBig = tBig > 0 ? tBig - 1 : 12 * 3600;
    tSmall = tSmall > 0 ? tSmall - 1 : 12 * 3600;
    var a = document.getElementById('numBig');
    var b = document.getElementById('numSmall');
    if (a) a.textContent = fmt(tBig);
    if (b) b.textContent = fmt(tSmall);
  }

  /* -------------------------------------------------------------- 启动 --- */
  if (new URLSearchParams(location.search).get('only') === 'stage') {
    document.body.classList.add('stage-only');
  }
  buildCompare();
  var initial = (location.hash || '').slice(1);
  applyTheme(META[initial] ? initial : 'quiet');
  setInterval(tick, 1000);

  document.addEventListener('keydown', function (e) {
    if (e.key !== 'ArrowRight' && e.key !== 'ArrowLeft') return;
    var i = ORDER.indexOf(cur);
    i = (i + (e.key === 'ArrowRight' ? 1 : ORDER.length - 1)) % ORDER.length;
    applyTheme(ORDER[i]);
  });

  window.applyTheme = applyTheme;
})();
