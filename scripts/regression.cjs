const assert = require('node:assert/strict');
const { execFileSync } = require('node:child_process');
const path = require('node:path');
const frontend = process.env.SMARTBOOKING_FE_PATH || path.resolve(__dirname, '../../SmartBooking-Football-FE');
const { chromium } = require(require.resolve('playwright-core', { paths: [frontend] }));
const web = process.env.SMARTBOOKING_DEMO_WEB;
const api = process.env.SMARTBOOKING_DEMO_API;
const password = 'RegressionLocal2026!';
const adminEmail = process.env.SMARTBOOKING_TEST_ADMIN;
const adminPassword = process.env.SMARTBOOKING_TEST_ADMIN_PASSWORD;
const webhookKey = process.env.SMARTBOOKING_TEST_WEBHOOK;
for (const target of [web, api]) {
  const address = new URL(target);
  assert.equal(address.protocol, 'http:');
  assert.equal(address.hostname, '127.0.0.1');
}
const calls = [];
const vietnamDate = day => new Intl.DateTimeFormat('sv-SE',{timeZone:'Asia/Ho_Chi_Minh'}).format(new Date(Date.now()+day*86400000));
const day = vietnamDate(3);
const futureDay = vietnamDate(4);
const query = values => '?' + new URLSearchParams(values);
const get = (path, expected, token) => apiCall('GET',path,expected,token);
const post = (path, expected, token, body) => apiCall('POST',path,expected,token,body);
const put = (path, expected, token, body) => apiCall('PUT',path,expected,token,body);
const del = (path, expected, token) => apiCall('DELETE',path,expected,token);
async function apiCall(method, path, expected, token, body) {
  const response = await fetch(api + (path === '/health' ? '' : '/api') + path, {
    redirect: 'manual',
    method, headers: {
      ...(token && { Authorization: token.startsWith('Apikey ') ? token : 'Bearer ' + token }),
      ...(body && { 'Content-Type': 'application/json' }),
    },
    ...(body && {body:JSON.stringify(body)}),
    signal: AbortSignal.timeout(22000),
  });
  const data = await response.json().catch(()=>null);
  assert(expected === undefined || response.status === expected,
    method+' '+path+' expected '+expected+', got '+response.status+' '+(data?.message||data?.detail||''));
  calls.push({method,path:(path === '/health' ? '' : '/api') + path.split('?')[0],status:response.status});
  return {status:response.status,data};
}
const credentials = name => ({email:name+'@example.test', fullName:'Automation '+name,password});
async function signIn(account,role) {
  const {data} = await post('/auth/login',200,undefined,account);
  assert.equal(data.role,role);
  return data.token;
}
async function until(work,expected,label) {
  for(let attempt=0;attempt<14;attempt++) {
    const result = await work();
    if(expected(result)) return result;
    await new Promise(resolve=>setTimeout(resolve,700));
  }
  throw new Error('Timed out: '+label);
}
const compose = ['compose','--env-file',process.env.SMARTBOOKING_DEMO_ENV,'--project-name',process.env.SMARTBOOKING_DEMO_PROJECT,
     '--file','compose.yaml','--file',process.env.SMARTBOOKING_DEMO_OVERRIDE];
assert.match(process.env.SMARTBOOKING_DEMO_PROJECT,/^football-rehearsal-[a-f0-9]{8}$/);
function sql(statement) {
  return execFileSync('docker',[...compose,'exec','-T','mysql','sh','-c',
    'MYSQL_PWD="$MYSQL_ROOT_PASSWORD" exec mysql -uroot booking_db --batch --skip-column-names'],
    {input:statement+'\n',timeout:16000,encoding:'utf8'});
}
const quote = (id,hour,date=day,expected=200) => get('/branches/'+id+'/quote'+query({date,hour}),expected);
const free = async (id,hour) => (await get('/pitches/'+id+'/availability'+query({date:day}),200)).data.find(row=>row.hour===hour)?.available;
const detail = (id,user) => get('/bookings/'+id,200,user).then(result=>result.data);

(async()=>{
  assert.equal((await get('/health',200)).data.status,'ok');
  await get('/bookings',401);
  await get('/notifications',401,'bad.jwt');
  await post('/auth/login',401,undefined,{email:'unknown@example.test',password});
  await post('/auth/register',400,undefined,{email:'invalid',fullName:'QA',password:'short'});
  const one = credentials('regression-user-one'), two = credentials('regression-user-two');
  assert.equal((await post('/auth/register',201,undefined,one)).data.role,'User');
  await post('/auth/register',409,undefined,one);
  await post('/auth/register',201,undefined,two);
  const user = await signIn(one,'User'), other = await signIn(two,'User');
  const admin = await signIn({email:adminEmail,password:adminPassword},'Admin');
  await get('/admin/branches',403,user);
  await get('/management/bookings',403,user);
  console.log('PASS auth: registration, duplicate/invalid input, JWT errors, User and Admin permissions.');

  const baseBranches = (await get('/branches',200)).data;
  assert.equal(baseBranches.length,3);
  const tanBinh = baseBranches.find(branch=>branch.area==='Tân Bình');
  assert.equal((await get('/branches'+query({search:'Cộng Hòa',area:'Tân Bình'}),200)).data[0].id,tanBinh.id);
  const firstPage = (await get('/branches?page=1&size=1',200)).data;
  const secondPage = (await get('/branches?page=2&size=1',200)).data;
  assert.notEqual(firstPage[0].id,secondPage[0].id);
  assert.equal((await get('/branches/'+tanBinh.id+'/pitches',200)).data.length,3);
  await get('/pitches/999999/availability?date='+day,404);
  await get('/pitches/1/availability?date=1900-99-99',400);
  await quote(tanBinh.id,0,day,400);
  assert.equal((await quote(tanBinh.id,17)).data.amount,250000);
  assert.equal((await quote(tanBinh.id,19)).data.amount,340000);
  assert.equal((await quote(tanBinh.id,22)).data.amount,250000);
  console.log('PASS catalogue: search, area, pagination, availability and Pricing boundaries.');

  const branchInput = {name:'QA Arena',address:'123 QA Street',area:'QA District',openingHour:6,closingHour:23,isActive:true};
  await post('/admin/branches',400,admin,{...branchInput,closingHour:6});
  const main = (await post('/admin/branches',201,admin,branchInput)).data;
  const next = (await post('/admin/branches',201,admin,{...branchInput,name:'QA Secondary'})).data;
  assert.equal((await put('/admin/branches/'+main.id,200,admin,{...branchInput,address:'456 QA Street'})).data.address,'456 QA Street');
  assert((await get('/admin/branches',200,admin)).data.some(row=>row.id===main.id));
  const managerAccount = credentials('regression-manager');
  await post('/admin/managers',403,user,managerAccount);
  const managerRecord = (await post('/admin/managers',201,admin,managerAccount)).data;
  assert.equal(managerRecord.role,'Manager');
  const manager = await signIn(managerAccount,'Manager');
  assert.equal((await get('/management/branches',200,manager)).data.length,0);
  await put('/admin/branches/'+main.id+'/manager',204,admin,{managerId:managerRecord.id});
  assert((await get('/admin/managers',200,admin)).data.some(row=>row.id===managerRecord.id&&row.branchId===main.id));
  assert.deepEqual((await get('/management/branches',200,manager)).data.map(branch=>branch.id),[main.id]);
  await get('/management/branches/'+next.id+'/pitches',403,manager);
  assert((await get('/management/branches',200,admin)).data.length>=5);
  const pitches = '/management/branches/'+main.id+'/pitches';
  await post(pitches,400,manager,{name:' ',isActive:true});
  const p1 = (await post(pitches,201,manager,{name:'QA Pitch One',isActive:true})).data;
  const p2 = (await post(pitches,201,manager,{name:'QA Pitch Two',isActive:true})).data;
  const p3 = (await post(pitches,201,manager,{name:'QA Pitch Three',isActive:true})).data;
  await post(pitches,409,manager,{name:'QA Fourth Pitch',isActive:true});
  assert.deepEqual((await get(pitches,200,manager)).data.map(row=>row.pitchNumber),[1,2,3]);
  await put('/management/pitches/'+p3.id,200,manager,{name:'QA Pitch Disabled',isActive:false});
  await post(pitches,409,manager,{name:'QA Fourth Pitch',isActive:true});
  assert(!(await get('/branches/'+main.id+'/pitches',200)).data.some(row=>row.id===p3.id));
  await get('/pitches/'+p3.id+'/availability?date='+day,404);
  await put('/management/pitches/'+p3.id,200,manager,{name:'QA Pitch Three',isActive:true});
  await del('/admin/branches/'+main.id+'/manager',204,admin);
  await get(pitches,403,manager);
  await put('/admin/branches/'+main.id+'/manager',204,admin,{managerId:managerRecord.id});
  console.log('PASS management API: branch create/update, Manager assignment/unassignment, pitch create/edit/disable and branch isolation.');

  const policies = '/management/branches/'+main.id+'/prices';
  await quote(main.id,17,day,404);
  await get('/management/branches/'+next.id+'/prices',403,manager);
  const price = {effectiveFrom:day,regularPriceVnd:260000,peakPriceVnd:360000,peakStartHour:18,peakEndHour:22};
  await post(policies,400,manager,{...price,peakPriceVnd:200000});
  const basePolicy = (await post(policies,200,manager,price)).data;
  await post(policies,409,manager,price);
  const futurePolicy = (await post(policies,200,manager,{...price,effectiveFrom:futureDay,regularPriceVnd:265000})).data;
  assert.equal((await get(policies,200,manager)).data.length,2);
  assert.equal((await quote(main.id,17)).data.amount,260000);
  assert.equal((await quote(main.id,17,futureDay)).data.amount,265000);
  await put(policies+'/'+basePolicy.id,200,manager,{...price,regularPriceVnd:270000});
  assert.equal((await quote(main.id,17)).data.amount,270000);
  assert.equal((await quote(main.id,19)).data.amount,360000);
  await del(policies+'/'+futurePolicy.id,204,manager);
  assert.equal((await quote(main.id,17,futureDay)).data.amount,270000);
  await del(policies+'/'+futurePolicy.id,404,manager);
  console.log('PASS Pricing CRUD: effective dates, changed prices, validation, duplicate date, delete and fallback.');

  const payload = (pitch,hour,expectedAmountVnd=270000) => ({pitchId:pitch.id,date:day,hour,expectedAmountVnd});
  assert.equal((await post('/bookings',409,user,payload(p1,16,250000))).data.code,'PriceChanged');
  assert.equal((await get('/bookings',200,user)).data.length,0);
  assert.equal(await free(p1.id,16),true);
  await post('/bookings',400,user,payload(p1,24));
  await post('/bookings',403,manager,payload(p1,16));
  const race = await Promise.all([post('/bookings',undefined,user,payload(p1,17)),post('/bookings',undefined,other,payload(p1,17))]);
  assert.deepEqual(race.map(row=>row.status).sort(),[201,409]);
  const [owner,stranger] = race[0].status===201?[user,other]:[other,user];
  const hold = race.find(row=>row.status===201).data;
  const id = hold.booking.id;
  assert.equal(hold.booking.status,'PendingPayment');
  assert.equal(hold.payment.amountVnd,135000);
  assert.equal(hold.payment.balanceAmountVnd,135000);
  assert.equal(hold.payment.totalAmountVnd,270000);
  assert(hold.payment.isDemo && !hold.payment.qrUrl);
  assert.equal(await free(p1.id,17),false);
  await get('/bookings/'+id,403,stranger);
  await post('/bookings/'+id+'/cancel',403,stranger);
  assert.equal((await get('/management/bookings/'+id,200,manager)).data.booking.id,id);
  const seededManager = await signIn({email:'manager.tanbinh@smartbooking.demo',password:'AuditFootball2026!'},'Manager');
  await get('/management/bookings/'+id,403,seededManager);

  const cancelled = (await post('/bookings',201,owner,payload(p2,16))).data.booking.id;
  await post('/bookings/'+cancelled+'/cancel',204,owner);
  await post('/bookings/'+cancelled+'/cancel',204,owner);
  assert.equal(await free(p2.id,16),true);
  const oldClient = await post('/bookings',201,owner,{pitchId:p2.id,date:day,hour:16});
  assert.equal(oldClient.data.payment.amountVnd,135000);
  await get('/bookings/999999',404,owner);
  const staffList = (await get('/management/bookings'+query({date:day,branchId:main.id,status:'PendingPayment',sort:'asc'}),200,manager)).data;
  assert(staffList.some(row=>row.booking.id===id));
  assert.deepEqual(staffList.map(row=>row.booking.startAtUtc),staffList.map(row=>row.booking.startAtUtc).sort());
  const paged = (await get('/bookings'+query({date:day,size:1,page:1,sort:'desc'}),200,owner)).data;
  assert.equal(paged.length,1);
  console.log('PASS booking API: price reconfirmation, concurrent collision, ownership, cancel/rebook, sort/filter/paging and legacy clients.');

  const webhook = '/payments/sepay/webhook';
  const transfer = {id:100001,accountNumber:'0000000000',content:hold.payment.code,transferType:'in',transferAmount:135000};
  const valid = 'Apikey '+webhookKey;
  await post(webhook,401,'Apikey invalid-demo-key',transfer);
  await post(webhook,400,valid,{...transfer,id:0});
  await post(webhook,200,valid,{...transfer,accountNumber:'0123456789'});
  await post(webhook,200,valid,{...transfer,transferType:'out'});
  await post(webhook,200,valid,{...transfer,content:'no booking code'});
  assert.equal((await detail(id,owner)).payment.status,'Pending');
  await post(webhook,200,valid,{...transfer,transferAmount:134000});
  await post(webhook,200,valid,{...transfer,transferAmount:134000});
  assert.equal((await detail(id,owner)).booking.status,'PendingPayment');
  await post(webhook,200,valid,{...transfer,id:100002});
  await post(webhook,200,valid,{...transfer,id:100002});
  await post(webhook,200,valid,{...transfer,id:100003});
  const paid = await detail(id,owner);
  assert.equal(paid.booking.status,'Confirmed');
  assert.equal(paid.payment.status,'Paid');
  assert.equal(paid.payment.paidAmountVnd,135000);
  assert.equal(paid.payment.remainingAmountVnd,135000);
  await post('/bookings/'+id+'/cancel',409,owner);
  const receipts = (await get('/admin/payment-receipts',200,admin)).data.filter(row=>row.bookingId===id);
  assert.deepEqual(receipts.map(row=>row.outcome).sort(),['AmountMismatch','Closed']);
  await get('/admin/payment-receipts',403,manager);

  const notice = await until(async()=>{
    const {data} = await get('/notifications?page=1&size=100',200,owner);
    return data.filter(row=>row.bookingId===id);
  }, rows=>rows.length===1,'paid booking notification');
  assert.equal(notice[0].readAtUtc,null);
  await post('/notifications/'+notice[0].id+'/read',404,stranger);
  await post('/notifications/'+notice[0].id+'/read',204,owner);
  await post('/notifications/'+notice[0].id+'/read',204,owner);
  const reread = (await get('/notifications',200,owner)).data.filter(row=>row.bookingId===id);
  assert.equal(reread.length,1);
  assert(reread[0].readAtUtc);
  await until(async()=> (await get('/notifications',200,owner)).data.some(row=>row.bookingId===cancelled),Boolean,'cancel notification');
  console.log('PASS SePay/Redis: signature, ignored transfers, mismatch, success, duplicates, late payment, receipts, notification and idempotent read.');

  await post('/management/bookings/'+id+'/check-in',403,owner);
  await post('/management/bookings/'+id+'/check-in',403,seededManager);
  await post('/management/bookings/'+oldClient.data.booking.id+'/check-in',409,manager);
  await post('/management/bookings/'+id+'/collect-balance',409,manager);
  const checkIns = await Promise.all([
    post('/management/bookings/'+id+'/check-in',204,manager),
    post('/management/bookings/'+id+'/check-in',204,admin),
  ]);
  assert.equal(checkIns.length,2);
  assert.equal((await detail(id,owner)).booking.status,'CheckedIn');
  await post('/management/bookings/'+id+'/collect-balance',403,owner);
  await post('/management/bookings/'+id+'/collect-balance',403,seededManager);
  await Promise.all([
    post('/management/bookings/'+id+'/collect-balance',204,manager),
    post('/management/bookings/'+id+'/collect-balance',204,admin),
  ]);
  const settled = await detail(id,owner);
  assert.equal(settled.payment.balanceStatus,'Paid');
  assert.equal(settled.payment.remainingAmountVnd,0);
  assert.equal(settled.payment.paidAmountVnd,270000);
  assert(settled.booking.checkedInAtUtc && settled.payment.balancePaidAtUtc);

  await put(policies+'/'+basePolicy.id,200,manager,{...price,regularPriceVnd:270001});
  const odd = (await post('/bookings',201,owner,payload(p3,15,270001))).data;
  assert.equal(odd.payment.amountVnd,135001);
  assert.equal(odd.payment.balanceAmountVnd,135000);
  await post('/bookings/'+odd.booking.id+'/cancel',204,owner);
  await put(policies+'/'+basePolicy.id,200,manager,{...price,regularPriceVnd:270000});

  const late = (await post('/bookings',201,owner,payload(p3,14))).data;
  await post(webhook,200,valid,{...transfer,id:100020,content:late.payment.code});
  execFileSync('docker',[...compose,'stop','worker'],{timeout:25000,stdio:'pipe'});
  try {
    sql("UPDATE bookings SET startTime = DATE_FORMAT(UTC_TIMESTAMP()-INTERVAL 1 HOUR,'%Y-%m-%d %H:00:00') WHERE id="+Number(late.booking.id)+';');
    await post('/management/bookings/'+late.booking.id+'/check-in',409,manager);
    await post('/management/bookings/'+late.booking.id+'/check-in',409,manager);
    assert.equal((await detail(late.booking.id,owner)).booking.status,'NoShow');
  } finally {
    execFileSync('docker',[...compose,'start','worker'],{timeout:25000,stdio:'pipe'});
  }
  const absent = (await post('/bookings',201,owner,payload(p2,14))).data;
  await post(webhook,200,valid,{...transfer,id:100021,content:absent.payment.code});
  sql("UPDATE bookings SET startTime = DATE_FORMAT(UTC_TIMESTAMP()-INTERVAL 1 HOUR,'%Y-%m-%d %H:00:00') WHERE id="+Number(absent.booking.id)+';');
  const missed = await until(()=>detail(absent.booking.id,owner),row=>row.booking.status==='NoShow','Worker no-show');
  assert.equal(missed.payment.status,'Paid');
  assert.equal(missed.payment.balanceStatus,'Cancelled');
  assert.equal(missed.payment.remainingAmountVnd,0);
  await post('/management/bookings/'+absent.booking.id+'/collect-balance',409,manager);
  await until(async()=> (await get('/notifications',200,owner)).data.filter(row=>row.bookingId===absent.booking.id && row.message.includes('không được check-in')),
    notices=>notices.length===1,'no-show notification');
  assert.equal(Number(sql("SELECT COUNT(*) FROM outboxmessages WHERE bookingId="+Number(late.booking.id)+" AND eventType='BookingNoShow';").trim()),1);
  const legacy = sql("SELECT b.id FROM bookings b JOIN payments p ON p.bookingId=b.id WHERE p.code='SBABCDEF1234567890';").trim();
  assert(legacy,'The migration fixture must exist');
  const legacyDetail = (await get('/management/bookings/'+legacy,200,admin)).data;
  assert.equal(legacyDetail.payment.amountVnd,220000);
  assert.equal(legacyDetail.payment.balanceAmountVnd,0);
  assert.equal(legacyDetail.payment.balanceStatus,'NotRequired');
  console.log('PASS deposit and attendance: 50% rounding, scoped/idempotent check-in and collection, no-show deadline, Worker notification, legacy full-payment migration.');

  const expiring = (await post('/bookings',201,owner,payload(p3,16))).data;
  sql('UPDATE bookings SET expiresAt = UTC_TIMESTAMP(6)-INTERVAL 1 SECOND WHERE id='+Number(expiring.booking.id)+';');
  await until(()=>detail(expiring.booking.id,owner),row=>row.booking.status==='Expired'&&row.payment.status==='Expired','expired booking');
  assert.equal(await free(p3.id,16),true);
  sql("UPDATE bookings SET startTime = DATE_FORMAT(UTC_TIMESTAMP()-INTERVAL 3 HOUR,'%Y-%m-%d %H:00:00') WHERE id="+Number(id)+";");
  await until(()=>detail(id,owner),row=>row.booking.status==='Completed'&&row.payment.status==='Paid','completed booking');
  await del('/admin/branches/'+next.id,204,admin);
  assert(!(await get('/branches',200)).data.some(row=>row.id===next.id));
  await quote(next.id,17,day,404);
  console.log('PASS Worker: hold expiration, reopen slot, completed match, and branch deactivation.');

  const browser = await chromium.launch({executablePath:process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe',headless:true});
  try {
    const page = await browser.newPage({viewport:{width:1440,height:1000},locale:'vi-VN'});
    page.setDefaultTimeout(15000);
    const navigation = page.getByRole('navigation',{name:'Điều hướng chính'});
    async function loginInBrowser(account) {
      await page.getByRole('button',{name:'Đăng nhập',exact:true}).click();
      await page.getByLabel('Email',{exact:true}).fill(account.email);
      await page.getByLabel('Mật khẩu',{exact:true}).fill(account.password);
      await page.locator('.auth').getByRole('button',{name:'Đăng nhập',exact:true}).click();
      await page.getByRole('button',{name:'Đăng xuất'}).waitFor();
    }
    const errors=[];
    page.on('pageerror',error=>errors.push(error.message));
    await page.route('**/*',route=>new URL(route.request().url()).origin===web?route.continue():route.abort());
    await page.goto(web);
    await page.getByRole('button',{name:'Đăng nhập',exact:true}).click();
    await page.getByRole('button',{name:'Tạo tài khoản mới'}).click();
    const browserUser = credentials('regression-browser-user');
    await page.getByLabel('Họ tên',{exact:true}).fill(browserUser.fullName);
    await page.getByLabel('Email',{exact:true}).fill(browserUser.email);
    await page.getByLabel('Mật khẩu',{exact:true}).fill(browserUser.password);
    await page.getByRole('button',{name:'Đăng ký',exact:true}).click();
    await page.getByRole('button',{name:'Đăng xuất'}).waitFor();
    await page.locator('.hero-search').getByLabel('Khu vực').fill('Tân Bình');
    await page.locator('.hero-search').getByRole('button',{name:'Tìm sân'}).click();
    await page.waitForFunction(()=>document.querySelectorAll('button.branch').length===1);
    await page.getByRole('button',{name:/Cộng Hòa Football Tân Bình/}).waitFor();
    await page.getByRole('button',{name:'Xóa bộ lọc'}).click();
    await page.getByRole('button',{name:/QA Arena/}).click();
    await page.getByRole('button',{name:'QA Pitch Three',exact:true}).click();
    await page.getByLabel('Ngày đá',{exact:true}).fill(futureDay);
    execFileSync('docker.exe',[...compose,'stop','pricing'],{timeout:20000,stdio:'pipe'});
    try {
      await page.getByRole('button',{name:'19:00',exact:true}).click();
      await page.getByText('Dịch vụ bảng giá chưa sẵn sàng.',{exact:true}).waitFor({timeout:20000});
      assert(await page.getByRole('button',{name:'Giữ sân và đặt cọc'}).isDisabled());
    } finally {
      execFileSync('docker.exe',[...compose,'start','pricing'],{timeout:25000,stdio:'pipe'});
    }
    await until(()=>quote(main.id,19,futureDay,undefined).catch(()=>({status:503})),row=>row.status===200,'Pricing back online');
    await page.getByRole('button',{name:'Thử lại'}).click();
    await page.getByRole('complementary',{name:'Tóm tắt đặt sân'}).getByText('360.000 ₫',{exact:true}).waitFor();
    assert(await page.getByRole('button',{name:'Giữ sân và đặt cọc'}).isEnabled());
    await navigation.getByRole('button',{name:'Lịch đặt của tôi',exact:true}).click();
    await page.getByText('Chưa có lịch đặt.',{exact:true}).waitFor();
    await page.getByRole('button',{name:'Đăng xuất'}).click();
    await loginInBrowser(browserUser);
    await page.getByRole('button',{name:/QA Arena/}).click();
    await page.getByRole('button',{name:'QA Pitch Three',exact:true}).click();
    await page.getByLabel('Ngày đá',{exact:true}).fill(futureDay);
    await page.getByRole('button',{name:'19:00',exact:true}).click();
    const holdResponse = page.waitForResponse(response => response.request().method()==='POST' &&
      new URL(response.url()).pathname==='/api/bookings' && response.status()===201);
    await page.getByRole('button',{name:'Giữ sân và đặt cọc'}).click();
    const uiHold = await (await holdResponse).json();
    const uiId = uiHold.booking.id;
    assert.equal(uiHold.payment.amountVnd,180000);
    await page.getByRole('heading',{name:'Lịch đặt #'+uiId}).waitFor();
    await page.reload();
    await page.getByRole('heading',{name:'Lịch đặt #'+uiId}).waitFor();
    await page.getByText('Tiền cọc 50% cần chuyển',{exact:true}).waitFor();
    await navigation.getByRole('button',{name:'Thông báo',exact:true}).click();
    await page.getByText('Chưa có thông báo.',{exact:true}).waitFor();
    execFileSync('docker',[...compose,'stop','worker'],{timeout:25000,stdio:'pipe'});
    try {
      await post(webhook,200,valid,{...transfer,id:100030,content:uiHold.payment.code,transferAmount:uiHold.payment.amountVnd});
      await page.getByText('Chưa có thông báo.',{exact:true}).waitFor();
    } finally {
      execFileSync('docker',[...compose,'start','worker'],{timeout:25000,stdio:'pipe'});
    }
    await page.getByText('Thanh toán thành công. Lịch đặt #'+uiId+' đã xác nhận.',{exact:true}).waitFor({timeout:25000});
    await page.getByRole('button',{name:'Đánh dấu đã đọc'}).click();
    await page.getByRole('button',{name:'Đánh dấu đã đọc'}).waitFor({state:'hidden'});
    await navigation.getByRole('button',{name:'Lịch đặt của tôi',exact:true}).click();
    await page.getByRole('button',{name:new RegExp('#'+uiId+' · QA Pitch Three')}).getByText('Đã xác nhận',{exact:true}).waitFor();
    console.log('PASS User UI: sign-in, deposit amount, checkout reload recovery and delayed notification while the screen stays open.');
    await page.getByRole('button',{name:'Đăng xuất'}).click();
    await page.getByRole('button',{name:'Đăng nhập',exact:true}).click();
    await page.getByLabel('Email',{exact:true}).fill(adminEmail);
    await page.getByLabel('Mật khẩu',{exact:true}).fill(adminPassword);
    await page.locator('.auth').getByRole('button',{name:'Đăng nhập',exact:true}).click();
    await page.getByRole('heading',{name:'Quản lý chi nhánh'}).waitFor();
    const createForm = page.locator('form').filter({has:page.getByRole('heading',{name:'Tạo chi nhánh'})});
    await createForm.getByLabel('Tên',{exact:true}).fill('QA Branch from Browser');
    await createForm.getByLabel('Địa chỉ',{exact:true}).fill('UI Test Street');
    await createForm.getByLabel('Khu vực',{exact:true}).fill('UI Test District');
    await createForm.getByRole('button',{name:'Lưu chi nhánh'}).click();

    const browserBranch = await until(async()=> (await get('/admin/branches',200,admin)).data.find(row=>row.name==='QA Branch from Browser'),Boolean,'Admin branch form');
    const assignedBranchSelector = page.locator('section.panel').filter({has:page.getByRole('heading',{name:'Quản lý chi nhánh',exact:true})}).locator(':scope > label > select');
    await assignedBranchSelector.selectOption(String(browserBranch.id));
    await page.getByRole('button',{name:'Sửa chi nhánh đã chọn'}).click();
    const editForm = page.locator('form').filter({has:page.getByRole('heading',{name:'Sửa chi nhánh',exact:true})});
    await editForm.getByLabel('Địa chỉ',{exact:true}).fill('UI Test Edited Street');
    await editForm.getByRole('button',{name:'Lưu chi nhánh'}).click();
    await until(async()=> (await get('/admin/branches',200,admin)).data.find(row=>row.id===browserBranch.id)?.address,
      value=>value==='UI Test Edited Street','Admin edit form');
    const managerUi = credentials('regression-browser-manager');
    const managerForm = page.locator('form').filter({has:page.getByRole('heading',{name:'Tạo Manager',exact:true})});
    await managerForm.getByLabel('Họ tên',{exact:true}).fill(managerUi.fullName);
    await managerForm.getByLabel('Email',{exact:true}).fill(managerUi.email);
    await managerForm.getByLabel('Mật khẩu',{exact:true}).fill(managerUi.password);
    await managerForm.getByRole('button',{name:'Tạo tài khoản'}).click();
    const createdManager = await until(async()=> (await get('/admin/managers',200,admin)).data.find(row=>row.email===managerUi.email),
      Boolean,'Manager creation form');
    await page.locator('select[name=managerId]').selectOption(String(createdManager.id));
    await page.getByRole('button',{name:'Lưu phân công'}).click();
    await until(async()=> (await get('/admin/managers',200,admin)).data.find(row=>row.id===createdManager.id)?.branchId,
      id=>id===browserBranch.id,'Manager assignment form');

    await page.getByRole('button',{name:'Đối soát',exact:true}).click();
    await page.getByText('Sai số tiền',{exact:true}).first().waitFor();
    await page.getByText('Lịch không còn chờ thanh toán',{exact:true}).first().waitFor();
    await page.getByRole('button',{name:'Lịch toàn hệ thống'}).click();
    await page.getByText('#'+id+' · QA Pitch One',{exact:true}).waitFor();

    await page.getByRole('button',{name:'Đăng xuất'}).click();
    await page.getByRole('button',{name:'Đăng nhập',exact:true}).click();
    await page.getByLabel('Email',{exact:true}).fill(managerUi.email);
    await page.getByLabel('Mật khẩu',{exact:true}).fill(managerUi.password);
    await page.locator('.auth').getByRole('button',{name:'Đăng nhập',exact:true}).click();
    await page.getByRole('heading',{name:'Quản lý chi nhánh'}).waitFor();
    const branchControl = assignedBranchSelector;
    await branchControl.selectOption(String(browserBranch.id));
    assert.equal(await branchControl.locator('option').count(),2);
    await page.getByLabel('Tên sân mới',{exact:true}).fill('QA Manager Pitch');
    await page.getByRole('button',{name:'Thêm sân',exact:true}).click();
    const pitchForm = page.locator('.pitch-edit').filter({has:page.getByLabel('Tên sân số 1')});
    await pitchForm.getByLabel('Tên sân số 1').waitFor();
    await pitchForm.getByLabel('Tên sân số 1').fill('QA Manager Pitch Edited');
    await pitchForm.getByRole('button',{name:'Lưu sân'}).click();
    await until(async()=> (await get('/branches/'+browserBranch.id+'/pitches',200)).data.some(row=>row.name==='QA Manager Pitch Edited'),
      Boolean,'Manager pitch form');
    const pricesUi = page.locator('form').filter({has:page.getByLabel('Ngày hiệu lực')});
    await pricesUi.getByLabel('Ngày hiệu lực').fill(day);
    await pricesUi.getByLabel('Giá thường (VND)').fill('210000');
    await pricesUi.getByLabel('Giá cao điểm (VND)').fill('310000');
    await pricesUi.getByRole('button',{name:'Thêm bảng giá'}).click();
    await until(()=>quote(browserBranch.id,19,day).catch(()=>null),result=>result?.data?.amount===310000,'Manager pricing form');
    for (const [index,name] of ['QA Manager Pitch Two','QA Manager Pitch Three'].entries()) {
      await page.getByLabel('Tên sân mới',{exact:true}).fill(name);
      await page.getByRole('button',{name:'Thêm sân',exact:true}).click();
      const input = page.getByLabel('Tên sân số '+(index+2),{exact:true});
      await input.waitFor();
      assert.equal(await input.inputValue(),name);
    }
    assert(await page.getByRole('button',{name:'Thêm sân',exact:true}).isDisabled());
    await page.getByRole('button',{name:'Đăng xuất'}).click();
    await loginInBrowser(managerAccount);
    await navigation.getByRole('button',{name:'Lịch sân',exact:true}).click();
    await page.getByRole('heading',{name:'Lịch sân',exact:true}).waitFor();
    assert.equal(await page.getByLabel('Ngày đá',{exact:true}).inputValue(),vietnamDate(0));
    await page.getByLabel('Ngày đá',{exact:true}).fill(futureDay);
    await page.getByLabel('Trạng thái',{exact:true}).selectOption('Confirmed');
    const court = page.getByRole('region',{name:'QA Pitch Three',exact:true});
    await court.getByRole('button',{name:new RegExp('#'+uiId)}).click();
    const bookingPanel = page.getByRole('complementary',{name:'Chi tiết lịch đặt'});
    await bookingPanel.getByRole('button',{name:'Check-in khách đến sân',exact:true}).click();
    await bookingPanel.getByText('Đã check-in',{exact:true}).waitFor();
    await bookingPanel.getByRole('button',{name:/Xác nhận đã thu.*tại sân/}).click();
    await bookingPanel.getByText(/180.000.*Đã thu/).waitFor();
    const collected = await detail(uiId,await signIn(browserUser,'User'));
    assert.equal(collected.payment.remainingAmountVnd,0);
    assert.equal(collected.booking.status,'CheckedIn');
    await page.getByRole('button',{name:'Đóng chi tiết'}).click();
    await page.getByRole('button',{name:'Hôm nay',exact:true}).click();
    assert.equal(await page.getByLabel('Ngày đá',{exact:true}).inputValue(),vietnamDate(0));
    console.log('PASS Manager/Admin UI: branch CRUD, assignment, pricing, three-pitch capacity, court schedule, check-in and balance collection.');
    assert.deepEqual(errors,[]);
  } finally { await browser.close(); }

  console.log('PASS additional browser flows: signup, search, Pricing outage/retry, personal schedule, admin branch edit/assign, reconciliation, system schedules, Manager pitch edit and pricing forms.');

  const schema = await fetch(api+'/swagger/v1/swagger.json',{signal:AbortSignal.timeout(10000)}).then(r=>r.json());
  const routes = Object.entries(schema.paths).flatMap(([template,verbs])=>Object.keys(verbs)
    .filter(verb=>['get','post','put','patch','delete'].includes(verb))
    .map(verb=>({method:verb.toUpperCase(),template,regex:new RegExp('^'+template.split('/').map(segment=>segment.startsWith('{')?'[^/]+':segment).join('/')+'/?$')})));
  const untested = routes.filter(route=>!calls.some(call=>call.method===route.method&&route.regex.test(call.path)));
  assert.deepEqual(untested.map(row=>row.method+' '+row.template),[]);
  console.log('All '+routes.length+' Swagger operations exercised over '+calls.length+' API requests in the regression suite.');
})().catch(error=>{console.error(error);process.exitCode=1;});
